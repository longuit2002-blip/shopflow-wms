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
        byte[] Secret,
        string ChannelType
    );

    public IReadOnlyList<ProvisionedTenant> Tenants => _tenants;
    public ProvisionedTenant this[int i] => _tenants[i];

    /// <summary>
    /// Provision <paramref name="tenantCount"/> tenants, register their
    /// channels in the catalog, then build the in-process Channel.Api
    /// host. Returns the harness ready for <see cref="SendAsync"/> calls.
    /// <paramref name="channelType"/> defaults to <c>"shopee"</c>;
    /// finish-line U7 passes <c>"lazada"</c> for the second-channel
    /// receive round-trip.
    /// </summary>
    public async Task InitializeAsync(
        int tenantCount = 5,
        string channelType = "shopee",
        CancellationToken ct = default
    )
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
            var tenant = await ProvisionTenantAsync(i, channelType, ct);
            await RegisterTenantInCatalogAsync(controlConnStr, tenant, ct);
            _tenants.Add(tenant);
        }

        var tenantTemplate = BuildTenantTemplate();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            // UseSetting writes to the web-host configuration with higher
            // precedence than appsettings.json. ConfigureAppConfiguration's
            // AddInMemoryCollection did NOT override the appsettings default
            // TenantTemplate (Host=localhost;Port=6432 PgBouncer), so the
            // catalog resolved tenant DBs to 6432 and the receive tests threw
            // "Failed to connect to [::1]:6432" — a never-run harness-config
            // gap. UseSetting is the reliable override surface here.
            builder.UseSetting("ControlPlane:ConnectionString", controlConnStr);
            builder.UseSetting("ControlPlane:TenantTemplate", tenantTemplate);
            builder.UseSetting("MessageBus:Transport", "InMemory");
            // The Channel.Api WAF boots non-Development, where AddShopFlowDefaults'
            // Sprint-9 KTD7 guard throws unless a ForwardedHeaders allowlist is
            // configured (it reads this config key directly, not IWebHostEnvironment).
            // Trust loopback — same posture as AuthAdminAuthorizationFixture /
            // HandoffFixture / MultiTenantAuthFixture. (The KTD7 guard postdates the
            // Sprint-5 base the U7 subagent built on, so its harness omitted this.)
            builder.UseSetting("Auth:ForwardedHeaders:KnownNetworks:0", "127.0.0.0/8");
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

        var url = $"/api/channel/webhooks/{target.ChannelType}/{target.ChannelId}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        req.Headers.Add("X-Shopee-Signature", signature);

        return await _client.SendAsync(req, ct);
    }

    /// <summary>
    /// Finish-line U7 — sign + POST a Lazada-shape webhook for
    /// <paramref name="tenantIndex"/>. Builds the Lazada
    /// <c>{event_id, event_type, data:{order_id, order_items[], delivery_carrier}}</c>
    /// envelope and signs it under the <c>X-Lazada-Signature</c> header,
    /// exercising the K8 channel-agnostic signature extraction path.
    /// Pass <paramref name="eventId"/> to force a fixed event_id for replay
    /// idempotency tests; pass <paramref name="signWithTenantIndex"/> to
    /// sign with a foreign tenant's secret (cross-tenant negative test);
    /// pass <paramref name="omitSignatureHeader"/> to send no signature
    /// header at all (missing-signature negative test).
    /// </summary>
    public async Task<HttpResponseMessage> SendLazadaAsync(
        int tenantIndex,
        string eventType,
        string orderId,
        (string ExternalSku, int Qty)[]? items = null,
        string deliveryCarrier = "LEX",
        int? signWithTenantIndex = null,
        string? eventId = null,
        bool omitSignatureHeader = false,
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

        var bodyBytes = BuildLazadaBody(orderId, eventType, items, deliveryCarrier, eventId);
        var signature = SignedWebhookSender.Sign(bodyBytes, signerSecret);

        using var content = new ByteArrayContent(bodyBytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/json"
        );

        var url = $"/api/channel/webhooks/{target.ChannelType}/{target.ChannelId}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (!omitSignatureHeader)
        {
            req.Headers.Add("X-Lazada-Signature", signature);
        }

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
        // Finish-line U7 — column names match the real product_mappings
        // schema (ProductMappingConfiguration): mapping_method +
        // confidence_score, NOT method/confidence. The original harness
        // INSERT drifted from the schema and threw 42703 the first time
        // these Docker-backed facts actually ran.
        cmd.CommandText =
            @"INSERT INTO product_mappings
              (id, channel_id, external_sku, internal_sku, mapping_method, confidence_score, created_at)
              VALUES (@id, @channel_id, @external_sku, @internal_sku, @mapping_method, @confidence_score, @created_at)";
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("channel_id", tenant.ChannelId);
        cmd.Parameters.AddWithValue("external_sku", externalSku);
        cmd.Parameters.AddWithValue("internal_sku", internalSku);
        cmd.Parameters.AddWithValue("mapping_method", MappingMethod.Manual.ToString());
        cmd.Parameters.AddWithValue("confidence_score", 1.0m);
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

    private async Task<ProvisionedTenant> ProvisionTenantAsync(
        int index,
        string channelType,
        CancellationToken ct
    )
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
            Secret: secret,
            ChannelType: channelType
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
            channelType: tenant.ChannelType,
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

    private static byte[] BuildLazadaBody(
        string orderId,
        string eventType,
        (string ExternalSku, int Qty)[]? items,
        string deliveryCarrier,
        string? eventId = null
    )
    {
        eventId ??= $"evt-{Guid.NewGuid():N}";

        var payload = new Dictionary<string, object?>
        {
            ["event_id"] = eventId,
            ["event_type"] = eventType,
            ["data"] = BuildLazadaDataObject(orderId, items, deliveryCarrier),
        };

        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    private static Dictionary<string, object?> BuildLazadaDataObject(
        string orderId,
        (string ExternalSku, int Qty)[]? items,
        string deliveryCarrier
    )
    {
        var data = new Dictionary<string, object?>
        {
            ["order_id"] = orderId,
            ["delivery_carrier"] = deliveryCarrier,
        };

        if (items is not null)
        {
            data["order_items"] = items
                .Select(it => new Dictionary<string, object?>
                {
                    ["sku"] = it.ExternalSku,
                    ["quantity"] = it.Qty,
                })
                .ToArray();
        }
        else
        {
            data["order_items"] = new[]
            {
                new Dictionary<string, object?> { ["sku"] = "LZ-DEFAULT", ["quantity"] = 1 },
            };
        }

        return data;
    }
}
