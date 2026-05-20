using FluentAssertions;
using Npgsql;
using Xunit;

namespace ShopFlow.Auth.IntegrationTests.Migrations;

/// <summary>
/// Sprint-8 U3 migration smoke. Asserts the three SQL-only assets
/// declared in <c>20260520000001_AddUsers</c> actually land in the
/// per-tenant DB after <c>MigrateAsync()</c>:
/// <list type="bullet">
///   <item><see cref="UxUsersEmailLowerExists"/> — UNIQUE expression
///   index on <c>lower(email)</c> the repo's case-insensitive lookup
///   depends on.</item>
///   <item><see cref="ChkUsersRoleEnumeratesShippedRoles"/> — CHECK
///   constraint mirroring the C# <c>UserRole</c> enum; would surface
///   any silent agreement drift between enum + SQL.</item>
///   <item><see cref="IxUsersRoleActivePartialIndexExists"/> — partial
///   index supporting Owner-only "list active users of role R"
///   listings in U8.</item>
/// </list>
/// Guards AGENTS.md §3.23: without the [Migration] + [DbContext]
/// attributes <c>MigrateAsync</c> would be a silent no-op — fixture
/// provisioning calls <c>MigrateAsync</c>, so these tests run AFTER
/// that and would fail-loud if the migration was skipped.
/// </summary>
[Collection(AuthTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AddUsersMigrationSmokeTests : IAsyncLifetime
{
    private readonly AuthTenantFixture _fx;
    private ProvisionedAuthTenant _tenant = default!;

    public AddUsersMigrationSmokeTests(AuthTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("smoke");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UsersTable_HasTheExpectedColumnShape()
    {
        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT column_name FROM information_schema.columns "
            + "WHERE table_name = 'users' ORDER BY ordinal_position;";

        var columns = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        columns.Should().BeEquivalentTo(new[]
        {
            "id",
            "email",
            "password_hash",
            "role",
            "is_active",
            "last_login_at",
            "created_at",
            "updated_at",
        });
    }

    [Fact]
    public async Task UxUsersEmailLowerExists()
    {
        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT indexdef FROM pg_indexes "
            + "WHERE tablename = 'users' AND indexname = 'ux_users_email_lower';";
        var indexDef = (string?)await cmd.ExecuteScalarAsync();

        indexDef.Should().NotBeNullOrEmpty();
        indexDef!.Should().Contain("UNIQUE");
        indexDef.Should().Contain("lower(email");
    }

    [Fact]
    public async Task ChkUsersRoleEnumeratesShippedRoles()
    {
        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint "
            + "WHERE conname = 'chk_users_role';";
        var def = (string?)await cmd.ExecuteScalarAsync();

        def.Should().NotBeNullOrEmpty();
        def!.Should().Contain("Owner");
        def.Should().Contain("Picker");
        def.Should().Contain("Dispatcher");
    }

    [Fact]
    public async Task IxUsersRoleActivePartialIndexExists()
    {
        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT indexdef FROM pg_indexes "
            + "WHERE tablename = 'users' AND indexname = 'ix_users_role_active';";
        var def = (string?)await cmd.ExecuteScalarAsync();

        def.Should().NotBeNullOrEmpty();
        def!.Should().Contain("is_active = true", because: "partial index predicate");
    }

    [Fact]
    public async Task UndefinedRoleString_IsRejectedByCheckConstraint()
    {
        // Verify the CHECK constraint actually bites — try to insert
        // a row with a junk role via raw SQL. The constraint should
        // fire as a 23514 (check_violation).
        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO users (id, email, password_hash, role, is_active, created_at) "
            + "VALUES (@id, @email, @hash, 'JunkRole', true, NOW());";
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("email", "junk@example.com");
        cmd.Parameters.AddWithValue("hash", "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA");

        var act = async () => await cmd.ExecuteNonQueryAsync();
        var pgEx = await act.Should().ThrowAsync<PostgresException>();
        pgEx.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }
}
