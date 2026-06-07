using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ShopFlow.Migrate.Provisioning;
using ShopFlow.SharedKernel.Authorization;
using Xunit;

namespace ShopFlow.Migrate.IntegrationTests.Provisioning;

/// <summary>
/// Sprint-11 U1 — closes the Sprint-9 U12 deferral. RolePermissionsSeed
/// against real Postgres: pins the additive-only contract (KTD1) by
/// exercising deletion-reversion and Owner-additions-preservation
/// scenarios end-to-end against a freshly-migrated tenant DB.
/// </summary>
/// <remarks>
/// <para>The seed body runs <c>INSERT … ON CONFLICT (role,
/// permission_key) DO NOTHING</c>. The three load-bearing properties
/// are: (1) re-seed never mutates an existing row's <c>created_at</c>;
/// (2) re-seed re-inserts any baseline row that was deleted between
/// runs; (3) any Owner addition beyond
/// <see cref="PermissionKeys.All"/> survives a re-seed unchanged.</para>
///
/// <para>CI runs against Testcontainers Postgres; locally Skip-marked
/// per Sprint-1+ posture (Docker required).</para>
/// </remarks>
[Collection(MigrateTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RolePermissionsSeedIntegrationTests : IAsyncLifetime
{
    private readonly MigrateTenantFixture _fx;
    private ProvisionedMigrateTenant _tenant = default!;

    public RolePermissionsSeedIntegrationTests(MigrateTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("perms");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static RolePermissionsSeed BuildSeed() => new(NullLogger<RolePermissionsSeed>.Instance);

    [Fact]
    public async Task SeedAsync_FreshTenant_Inserts_Owner_Picker_Dispatcher_And_Packer_Baseline_Rows()
    {
        var seed = BuildSeed();

        await seed.SeedAsync(_tenant.ConnectionString, CancellationToken.None);

        // Sprint-13 (AE1) — 24 Owner rows (every PermissionKeys.All entry)
        // + 4 Picker rows + 3 Dispatcher rows + 3 Packer rows = 34 total.
        // All four role counts are wired off live constants so the
        // assertion survives future baseline growth.
        var expectedTotal =
            PermissionKeys.All.Count
            + RolePermissionsSeed.PickerBaseline.Count
            + RolePermissionsSeed.DispatcherBaseline.Count
            + RolePermissionsSeed.PackerBaseline.Count;

        var total = await CountRowsAsync(_tenant.ConnectionString);
        total.Should().Be(expectedTotal);

        var pickerKeys = await ReadPermissionKeysAsync(_tenant.ConnectionString, "Picker");
        pickerKeys
            .Should()
            .BeEquivalentTo(
                new[]
                {
                    "outbound.orders.read",
                    "outbound.orders.pick-confirm",
                    "inventory.read",
                    "hub.connect",
                }
            );

        var dispatcherKeys = await ReadPermissionKeysAsync(_tenant.ConnectionString, "Dispatcher");
        dispatcherKeys
            .Should()
            .BeEquivalentTo(
                new[] { "outbound.orders.read", "outbound.orders.ship-confirm", "hub.connect" }
            );

        var packerKeys = await ReadPermissionKeysAsync(_tenant.ConnectionString, "Packer");
        packerKeys
            .Should()
            .BeEquivalentTo(
                new[] { "outbound.orders.read", "outbound.orders.pack-confirm", "hub.connect" }
            );

        var ownerKeys = await ReadPermissionKeysAsync(_tenant.ConnectionString, "Owner");
        ownerKeys.Should().BeEquivalentTo(PermissionKeys.All);
    }

    [Fact]
    public async Task SeedAsync_PreservesOwnerManualPackConfirmGrantOnPicker_AndPackerBaselineWritesClean()
    {
        // Sprint-13 AE9 — operator pre-grants outbound.orders.pack-confirm
        // to Picker via the admin editor BEFORE the Sprint-13 deploy.
        // Sprint-13 re-runs provisioning; the additive-only contract
        // (KTD1) leaves Picker's manual grant intact AND writes the Packer
        // baseline cleanly. BOTH Picker AND Packer end up with
        // pack-confirm — the documented operator-runbook scenario.
        var seed = BuildSeed();
        await seed.SeedAsync(_tenant.ConnectionString, CancellationToken.None);

        // Operator pre-Sprint-13 grants pack-confirm to Picker.
        await InsertRoleKeyAsync(
            _tenant.ConnectionString,
            "Picker",
            PermissionKeys.OutboundOrdersPackConfirm
        );

        // Sprint-13 re-runs provisioning.
        await seed.SeedAsync(_tenant.ConnectionString, CancellationToken.None);

        var pickerKeys = await ReadPermissionKeysAsync(_tenant.ConnectionString, "Picker");
        pickerKeys.Should().HaveCount(5);
        pickerKeys
            .Should()
            .Contain(
                PermissionKeys.OutboundOrdersPackConfirm,
                because: "KTD1 additive-only — Owner addition on Picker survives re-seed"
            );

        var packerKeys = await ReadPermissionKeysAsync(_tenant.ConnectionString, "Packer");
        packerKeys.Should().HaveCount(3);
        packerKeys
            .Should()
            .BeEquivalentTo(
                RolePermissionsSeed.PackerBaseline,
                because: "Packer baseline writes cleanly alongside the preserved Picker grant"
            );
    }

    [Fact]
    public async Task SeedAsync_PreservesOwnerAdditionsToDispatcher_AndDispatcherBaselinePreserved()
    {
        // Sprint-12 doc-review AE6 mitigation — operator pre-grants
        // outbound.orders.ship-confirm to Picker before Sprint-12
        // deploy. Sprint-12 re-runs provisioning; the additive-only
        // contract leaves Picker's manual grant intact AND writes the
        // Dispatcher baseline cleanly. Two roles end up with
        // ship-confirm — the documented operator-runbook scenario.
        var seed = BuildSeed();
        await seed.SeedAsync(_tenant.ConnectionString, CancellationToken.None);

        // Operator pre-Sprint-12 grants ship-confirm to Picker.
        await InsertRoleKeyAsync(
            _tenant.ConnectionString,
            "Picker",
            PermissionKeys.OutboundOrdersShipConfirm
        );

        // Sprint-12 re-runs provisioning.
        await seed.SeedAsync(_tenant.ConnectionString, CancellationToken.None);

        var pickerKeys = await ReadPermissionKeysAsync(_tenant.ConnectionString, "Picker");
        pickerKeys.Should().HaveCount(5);
        pickerKeys
            .Should()
            .Contain(
                PermissionKeys.OutboundOrdersShipConfirm,
                because: "KTD1 additive-only — Owner addition on Picker survives re-seed"
            );

        var dispatcherKeys = await ReadPermissionKeysAsync(_tenant.ConnectionString, "Dispatcher");
        dispatcherKeys.Should().HaveCount(3);
        dispatcherKeys
            .Should()
            .BeEquivalentTo(
                RolePermissionsSeed.DispatcherBaseline,
                because: "Dispatcher baseline writes cleanly alongside the preserved Picker grant"
            );
    }

    [Fact]
    public async Task SeedAsync_PreservesOwnerAdditionsBeyondBaseline()
    {
        // Additive-only contract — KTD1: an Owner addition made via the
        // admin editor (the Sprint-9.5 U7 surface) must survive a
        // subsequent provision-time re-seed unmodified. We model this
        // by adding an extra Picker key after the first seed; the
        // assertion shape is identical for Owner additions because the
        // INSERT path is shared.
        var seed = BuildSeed();
        await seed.SeedAsync(_tenant.ConnectionString, CancellationToken.None);

        await InsertRoleKeyAsync(_tenant.ConnectionString, "Picker", "inbound.pos.read");

        // Re-seed — additive-only: extra row must be preserved.
        await seed.SeedAsync(_tenant.ConnectionString, CancellationToken.None);

        var pickerKeys = await ReadPermissionKeysAsync(_tenant.ConnectionString, "Picker");
        pickerKeys.Should().HaveCount(5);
        pickerKeys.Should().Contain("inbound.pos.read");
        pickerKeys.Should().Contain(RolePermissionsSeed.PickerBaseline);
    }

    [Fact]
    public async Task SeedAsync_RevertsDeletionsOfBaselineRows()
    {
        // KTD1 semantic — a baseline row that was manually deleted
        // between runs must be re-inserted on the next seed.
        var seed = BuildSeed();
        await seed.SeedAsync(_tenant.ConnectionString, CancellationToken.None);

        await DeleteRoleKeyAsync(_tenant.ConnectionString, "Picker", PermissionKeys.InventoryRead);

        var afterDelete = await ReadPermissionKeysAsync(_tenant.ConnectionString, "Picker");
        afterDelete
            .Should()
            .NotContain(
                PermissionKeys.InventoryRead,
                because: "the manual DELETE removed the baseline row"
            );
        afterDelete.Should().HaveCount(3);

        // Re-seed — the baseline INSERT path must re-insert the
        // deleted row because ON CONFLICT only suppresses re-insertion
        // when the row already exists.
        await seed.SeedAsync(_tenant.ConnectionString, CancellationToken.None);

        var afterReseed = await ReadPermissionKeysAsync(_tenant.ConnectionString, "Picker");
        afterReseed.Should().HaveCount(4);
        afterReseed
            .Should()
            .Contain(
                PermissionKeys.InventoryRead,
                because: "the re-seed re-inserted the deleted baseline key"
            );
        afterReseed.Should().BeEquivalentTo(RolePermissionsSeed.PickerBaseline);
    }

    [Fact]
    public async Task SeedAsync_RunTwice_DoesNotMutate_CreatedAt_NorRowCount()
    {
        // No-mutation idempotency — re-seed must not bump created_at on
        // existing rows (ON CONFLICT DO NOTHING short-circuits before
        // any UPDATE). Catches accidental ON CONFLICT DO UPDATE drift.
        var seed = BuildSeed();
        await seed.SeedAsync(_tenant.ConnectionString, CancellationToken.None);

        var expectedTotal =
            PermissionKeys.All.Count
            + RolePermissionsSeed.PickerBaseline.Count
            + RolePermissionsSeed.DispatcherBaseline.Count
            + RolePermissionsSeed.PackerBaseline.Count;
        var firstSnapshot = await ReadRowSnapshotAsync(_tenant.ConnectionString);
        firstSnapshot.Should().HaveCount(expectedTotal);

        await seed.SeedAsync(_tenant.ConnectionString, CancellationToken.None);

        var secondSnapshot = await ReadRowSnapshotAsync(_tenant.ConnectionString);
        secondSnapshot
            .Should()
            .HaveCount(
                expectedTotal,
                because: "re-seed must not produce duplicate rows on the composite PK"
            );
        secondSnapshot
            .Should()
            .BeEquivalentTo(
                firstSnapshot,
                because: "ON CONFLICT DO NOTHING must leave existing created_at values untouched"
            );
    }

    // ---- helpers ----

    private static async Task<int> CountRowsAsync(string connStr)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM role_permissions;";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task<List<string>> ReadPermissionKeysAsync(string connStr, string role)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT permission_key FROM role_permissions WHERE role = @role ORDER BY permission_key;";
        cmd.Parameters.AddWithValue("role", role);

        var keys = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            keys.Add(reader.GetString(0));
        }
        return keys;
    }

    private static async Task<
        List<(string Role, string Key, DateTime CreatedAt)>
    > ReadRowSnapshotAsync(string connStr)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT role, permission_key, created_at FROM role_permissions ORDER BY role, permission_key;";

        var rows = new List<(string, string, DateTime)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetDateTime(2)));
        }
        return rows;
    }

    private static async Task InsertRoleKeyAsync(string connStr, string role, string key)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO role_permissions (role, permission_key, created_at) "
            + "VALUES (@role, @key, NOW());";
        cmd.Parameters.AddWithValue("role", role);
        cmd.Parameters.AddWithValue("key", key);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DeleteRoleKeyAsync(string connStr, string role, string key)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "DELETE FROM role_permissions WHERE role = @role AND permission_key = @key;";
        cmd.Parameters.AddWithValue("role", role);
        cmd.Parameters.AddWithValue("key", key);
        await cmd.ExecuteNonQueryAsync();
    }
}
