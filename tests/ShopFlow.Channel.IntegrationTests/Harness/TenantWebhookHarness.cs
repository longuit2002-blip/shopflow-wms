using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ShopFlow.Channel.Domain.ProductMappings;
using ShopFlow.Channel.Infrastructure;
using ShopFlow.ControlPlane.Domain;
using ShopFlow.ControlPlane.Infrastructure;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Channel.IntegrationTests.Harness;

/// <summary>
/// Sprint-4.5 plan U4 — multi-tenant webhook receiver harness. Provisions
/// N tenant DBs (control-plane + per-tenant Channel schemas), seeds
/// tenants + channels + signing secrets, and boots a
/// <see cref="WebApplicationFactory{TEntryPoint}"/>-backed
/// <c>Channel.Api</c> host pointing at the shared Testcontainers Postgres.
/// </summary>
/// <remarks>
/// <para>One harness instance per test class — provisions a fresh control
/// DB + N tenant DBs so cross-test contamination is impossible. The
/// <see cref="ChannelWebhookFixture"/> collection fixture amortizes the
/// expensive Postgres container start across many tests; provisioning
/// new databases on the same container is cheap.</para>
///
/// <para>MassTransit transport pinned to <c>InMemory</c> via the
/// <c>MessageBus:Transport</c> config key — Sprint-4.5 doesn't need a
/// real broker connection (the scale-gate tests measure the outbox-write
/// path, not the dispatch path). The dispatcher background service runs
/// but processes against the in-memory bus.</para>
///
/// <para>Test contract: <see cref="SendAsync"/> takes a tenant index +
/// payload + signing-secret choice (own tenant's secret for the happy
/// path; foreign tenant's secret for the cross-tenant negative test) and
/// posts a signed Shopee-shape webhook through the controller pipeline.
/// <see cref="CountWebhookEventsAsync"/> + <see cref="CountOutboxRowsAsync"/>
/// inspect each tenant's DB after the burst.</para>
/// </remarks>
public sealed class TenantWebhookHarness : IAsyncDisposable
{
    private readonly ChannelWebhookFixture _fixture;
    private readonly string _controlPlaneConnString;
    private readonly List<ProvisionedTenant> _tenants = new();
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public TenantWebhookHarness(ChannelWebhookFixture fixture)
    {
        _fixture = fixture;
        _controlPlaneConnString = string.Empty;
    }

    /// <summary>
    /// Provisioned tenant snapshot. <see cref="Secret"/> is the raw HMAC
    /// secret used to sign inbound webhooks — Sprint-4.5 treats the
    /// <c>channel_connections.secret_encrypted</c> column as raw bytes;
    /// the at-rest encryption layer is Phase-3+ work.
    /// </summary>
    public sealed record ProvisionedTenant(
        int Index,
        Guid TenantId,
        string Slug,
        string DbName,
        string DbConnectionString,
        Guid ChannelId,
        byte[] Secret
    );

    public IReadOnlyList<ProvisionedTenant> Tenants => _tenants;
    public ProvisionedTenant this[int i] => _tenants[i];

    /// <summary>
    /// Provision <paramref name="tenantCount"/> tenants, register their
    /// channels in the catalog, then build the in-process Channel.Api
    /// host. Returns the harness ready for <see cref="SendAsync"/> calls.
    /// </summary>
    public async Task InitializeAsync(int tenantCount = 5, CancellationToken ct = default)
    {
        if (tenantCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tenantCount));
        }

        var controlDbName = $"shopflow_control_{Guid.NewGuid().ToString("N")[..8]}";
        var controlConnStr = await _fixture.CreateDatabaseAsync(controlDbName, ct);
        await ApplyControlPlaneMigrationsAsync(controlConnStr, ct);

        for (var i = 0; i < tenantCount; i++)
        {
            var tenant = await ProvisionTenantAsync(i, ct);
            await RegisterTenantInCatalogAsync(controlConnStr, tenant, ct);
            _tenants.Add(tenant);
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(
                (_, cfg) =>
                {
                    cfg.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ControlPlane:ConnectionString"] = controlConnStr,
                            ["ControlPlane:TenantTemplate"] = BuildTenantTemplate(),
                            ["MessageBus:Transport"] = "InMemory",
                        }
                    );
                }
            );
        });

        _client = _factory.CreateClient();
    }

    /// <summary>
    /// Sign + POST a Shopee-shape webhook for <paramref name="tenantIndex"/>.
    /// The signing secret defaults to that tenant's own — pass
    /// <paramref name="signWithTenantIndex"/> to sign with a different
    /// tenant's secret (cross-tenant-signature negative test). Pass
    /// <paramref name="eventId"/> to force a fixed Shopee envelope
    /// event_id (replay-idempotency tests rely on this — without it
    /// each call generates a fresh GUID).
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(
        int tenantIndex,
        string eventType,
        string ordersn,
        (string ExternalSku, int Qty)[]? items = null,
        string shippingCarrier = "GHN",
        int? signWithTenantIndex = null,
        string? eventId = null,
        CancellationToken ct = default
    )
    {
        if (_client is null)
        {
            throw new InvalidOperationException(
                "Harness not initialised. Call InitializeAsync first."
            );
        }

        var target = _tenants[tenantIndex];
        var signerSecret = _tenants[signWithTenantIndex ?? tenantIndex].Secret;

        var bodyBytes = BuildShopeeBody(ordersn, eventType, items, shippingCarrier, eventId);
        var signature = SignedWebhookSender.Sign(bodyBytes, signerSecret);

        using var content = new ByteArrayContent(bodyBytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/json"
        );

        var url = $"/api/channel/webhooks/shopee/{target.ChannelId}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        req.Headers.Add("X-Shopee-Signature", signature);

        return await _client.SendAsync(req, ct);
    }

    /// <summary>Count <c>webhook_events</c> rows in a tenant's DB.</summary>
    public async Task<int> CountWebhookEventsAsync(int tenantIndex, CancellationToken ct = default)
    {
        return await CountTableRowsAsync(_tenants[tenantIndex], "webhook_events", ct);
    }

    /// <summary>Count <c>channel_outbox_messages</c> rows in a tenant's DB.</summary>
    public async Task<int> CountOutboxRowsAsync(int tenantIndex, CancellationToken ct = default)
    {
        return await CountTableRowsAsync(_tenants[tenantIndex], "channel_outbox_messages", ct);
    }

    /// <summary>
    /// Read all outbox <c>(event_type, payload)</c> rows for a tenant —
    /// useful when a test wants to assert the emitted contract shape.
    /// </summary>
    public async Task<IReadOnlyList<(string EventType, string Payload)>> GetOutboxRowsAsync(
        int tenantIndex,
        CancellationToken ct = default
    )
    {
        var rows = new List<(string, string)>();
        await using var conn = new NpgsqlConnection(_tenants[tenantIndex].DbConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT event_type, payload FROM channel_outbox_messages ORDER BY created_at";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }
        return rows;
    }

    /// <summary>Seed a manual product mapping for happy-path scale tests.</summary>
    public async Task SeedManualMappingAsync(
        int tenantIndex,
        string externalSku,
        string internalSku,
        CancellationToken ct = default
    )
    {
        var tenant = _tenants[tenantIndex];
        await using var conn = new NpgsqlConnection(tenant.DbConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            @"INSERT INTO product_mappings
              (id, channel_id, external_sku, internal_sku, method, confidence, created_at)
              VALUES (@id, @channel_id, @external_sku, @internal_sku, @method, @confidence, @created_at)";
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("channel_id", tenant.ChannelId);
        cmd.Parameters.AddWithValue("external_sku", externalSku);
        cmd.Parameters.AddWithValue("internal_sku", internalSku);
        cmd.Parameters.AddWithValue("method", MappingMethod.Manual.ToString());
        cmd.Parameters.AddWithValue("confidence", 1.0m);
        cmd.Parameters.AddWithValue("created_at", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
        NpgsqlConnection.ClearAllPools();
    }

    private string BuildTenantTemplate()
    {
        var template = new NpgsqlConnectionStringBuilder(_fixture.AdminConnectionString)
        {
            Database = "{db}",
            MaxPoolSize = 25,
        }.ConnectionString;
        // NpgsqlConnectionStringBuilder URL-encodes the literal "{db}" — undo it.
        return template
            .Replace("%7B", "{", StringComparison.OrdinalIgnoreCase)
            .Replace("%7D", "}", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ProvisionedTenant> ProvisionTenantAsync(int index, CancellationToken ct)
    {
        var slug = $"t{index}-{Guid.NewGuid().ToString("N")[..6]}";
        var dbName = $"shopflow_t_{slug}";
        var connStr = await _fixture.CreateDatabaseAsync(dbName, ct);

        var options = new DbContextOptionsBuilder<ChannelDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.Channel.Infrastructure"))
            .Options;
        await using (var ctx = new ChannelDbContext(options))
        {
            await ctx.Database.MigrateAsync(ct);
        }

        var secret = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(secret);

        return new ProvisionedTenant(
            Index: index,
            TenantId: Guid.NewGuid(),
            Slug: slug,
            DbName: dbName,
            DbConnectionString: connStr,
            ChannelId: Guid.NewGuid(),
            Secret: secret
        );
    }

    private static async Task ApplyControlPlaneMigrationsAsync(string connStr, CancellationToken ct)
    {
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.ControlPlane.Migrations"))
            .Options;
        await using var ctx = new ControlPlaneDbContext(options);
        await ctx.Database.MigrateAsync(ct);
    }

    private static async Task RegisterTenantInCatalogAsync(
        string controlConnStr,
        ProvisionedTenant tenant,
        CancellationToken ct
    )
    {
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(
                controlConnStr,
                npg => npg.MigrationsAssembly("ShopFlow.ControlPlane.Migrations")
            )
            .Options;
        await using var ctx = new ControlPlaneDbContext(options);

        var create = Tenant.Create(
            slug: tenant.Slug,
            dbName: tenant.DbName,
            region: "ap-southeast-1",
            tier: "free"
        );
        if (!create.IsSuccess)
        {
            throw new InvalidOperationException(
                $"failed to create tenant '{tenant.Slug}' in catalog: {create.Error}"
            );
        }
        // Force the tenant id to match the provisioned record so the
        // catalog ID matches what RequestContext.Bind will see at request time.
        var t = create.Value!;
        SetPrivateProperty(t, "Id", tenant.TenantId);

        // Walk through Pending → Provisioning → Ready so the routing
        // middleware accepts requests for this tenant.
        t.BeginProvisioning();
        t.MarkProvisioned();
        ctx.Tenants.Add(t);

        var channel = ChannelConnection.Create(
            channelId: tenant.ChannelId,
            tenantId: tenant.TenantId,
            channelType: "shopee",
            secretEncrypted: tenant.Secret
        );
        if (!channel.IsSuccess)
        {
            throw new InvalidOperationException(
                $"failed to create channel for tenant '{tenant.Slug}': {channel.Error}"
            );
        }
        ctx.ChannelConnections.Add(channel.Value!);
        await ctx.SaveChangesAsync(ct);
    }

    private static void SetPrivateProperty<T>(object instance, string propertyName, T value)
    {
        var prop =
            instance
                .GetType()
                .GetProperty(
                    propertyName,
                    System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance
                )
            ?? throw new InvalidOperationException(
                $"property '{propertyName}' not found on {instance.GetType().FullName}."
            );
        prop.SetValue(instance, value);
    }

    private static async Task<int> CountTableRowsAsync(
        ProvisionedTenant tenant,
        string tableName,
        CancellationToken ct
    )
    {
        await using var conn = new NpgsqlConnection(tenant.DbConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {tableName}";
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    private static byte[] BuildShopeeBody(
        string ordersn,
        string eventType,
        (string ExternalSku, int Qty)[]? items,
        string shippingCarrier,
        string? eventId = null
    )
    {
        eventId ??= $"evt-{Guid.NewGuid():N}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var payload = new Dictionary<string, object?>
        {
            ["event_id"] = eventId,
            ["event_type"] = eventType,
            ["shop_id"] = 42L,
            ["timestamp"] = timestamp,
            ["data"] = BuildDataObject(ordersn, items, shippingCarrier),
        };

        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    private static Dictionary<string, object?> BuildDataObject(
        string ordersn,
        (string ExternalSku, int Qty)[]? items,
        string shippingCarrier
    )
    {
        var data = new Dictionary<string, object?>
        {
            ["ordersn"] = ordersn,
            ["package_list"] = new[]
            {
                new Dictionary<string, object?> { ["shipping_carrier"] = shippingCarrier },
            },
        };

        if (items is not null)
        {
            data["items"] = items
                .Select(it => new Dictionary<string, object?>
                {
                    ["item_sku"] = it.ExternalSku,
                    ["model_quantity_purchased"] = it.Qty,
                })
                .ToArray();
        }
        else
        {
            data["items"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["item_sku"] = "SP-DEFAULT",
                    ["model_quantity_purchased"] = 1,
                },
            };
        }

        return data;
    }
}
