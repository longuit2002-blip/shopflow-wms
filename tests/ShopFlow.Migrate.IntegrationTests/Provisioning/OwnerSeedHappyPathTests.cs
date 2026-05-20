using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using ShopFlow.Auth.Application.Services;
using ShopFlow.Auth.Infrastructure.Hashing;
using ShopFlow.Migrate.Provisioning;
using Xunit;

namespace ShopFlow.Migrate.IntegrationTests.Provisioning;

/// <summary>
/// Sprint-8.5 U11 — closes the Sprint-8 U10 deferral. OwnerSeed
/// happy-path against real Postgres + Argon2id verify round-trip.
/// </summary>
/// <remarks>
/// <para>Sprint-8 U10 shipped <c>OwnerSeed.SeedAsync</c> with unit
/// coverage for flag-resolution + stdout-echo only; the seed row
/// shape + Argon2 round-trip + idempotency-on-duplicate-email path
/// were deferred to this integration suite. CI runs against
/// Testcontainers Postgres; locally Skip-marked per Sprint-1+
/// posture (Docker required).</para>
/// </remarks>
[Collection(MigrateTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OwnerSeedHappyPathTests : IAsyncLifetime
{
    private readonly MigrateTenantFixture _fx;
    private ProvisionedMigrateTenant _tenant = default!;

    public OwnerSeedHappyPathTests(MigrateTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("seed");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OwnerSeed BuildSeed() =>
        new(new PasswordGenerator(), NullLogger<OwnerSeed>.Instance);

    private static Argon2idPasswordHasher BuildHasher() =>
        new(Options.Create(new Argon2Options()));

    [Fact]
    public async Task SeedAsync_FreshTenant_InsertsOwnerRow_VerifiesViaArgon2()
    {
        var seed = BuildSeed();
        const string email = "owner@happy.local";

        var result = await seed.SeedAsync(
            _tenant.ConnectionString, email, explicitPassword: null, CancellationToken.None);

        // Outcome shape.
        result.Outcome.Should().Be(OwnerSeedOutcome.Seeded);
        result.OwnerEmail.Should().Be(email);
        result.GeneratedPassword.Should().NotBeNullOrEmpty();
        result.GeneratedPassword!.Length.Should().BeGreaterThanOrEqualTo(16);

        // DB row shape — raw SQL so we don't bind a DbContext just to
        // read four columns.
        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT email, role, is_active, password_hash "
            + "FROM users WHERE email = @e;";
        cmd.Parameters.AddWithValue("e", email);

        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue(because: "exactly one row was inserted");
        reader.GetString(0).Should().Be(email);
        reader.GetString(1).Should().Be("Owner");
        reader.GetBoolean(2).Should().BeTrue();
        var phcHash = reader.GetString(3);
        phcHash.Should().StartWith("$argon2id$v=19$", because: "OwnerSeed delegates to Argon2idPasswordHasher");

        // Argon2 round-trip — the PHC stored in the DB verifies against
        // the plaintext the seed echoed via OwnerSeedResult.
        var hasher = BuildHasher();
        hasher.Verify(result.GeneratedPassword, phcHash)
            .Should()
            .BeTrue(because: "the seeded user must be able to log in with the echoed temp password");

        // Single-row check.
        (await reader.ReadAsync()).Should().BeFalse(because: "exactly one row");
    }

    [Fact]
    public async Task SeedAsync_AlreadySeededEmail_IsIdempotent()
    {
        var seed = BuildSeed();
        const string email = "owner@idempotent.local";

        var first = await seed.SeedAsync(
            _tenant.ConnectionString, email, explicitPassword: null, CancellationToken.None);
        first.Outcome.Should().Be(OwnerSeedOutcome.Seeded);

        var second = await seed.SeedAsync(
            _tenant.ConnectionString, email, explicitPassword: null, CancellationToken.None);
        second.Outcome.Should().Be(OwnerSeedOutcome.AlreadySeeded);
        second.GeneratedPassword.Should().BeNull(because: "no new password generated on idempotent skip");

        // Single-row count — no duplicate INSERTed.
        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users WHERE email = @e;";
        cmd.Parameters.AddWithValue("e", email);
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        count.Should().Be(1);
    }

    [Fact]
    public async Task SeedAsync_ExplicitPassword_StoresProvidedSecretAndDoesNotEchoIt()
    {
        var seed = BuildSeed();
        const string email = "owner@explicit.local";
        const string explicitPwd = "MyExplicitPassword123!";

        var result = await seed.SeedAsync(
            _tenant.ConnectionString, email, explicitPassword: explicitPwd, CancellationToken.None);

        result.Outcome.Should().Be(OwnerSeedOutcome.Seeded);
        result.GeneratedPassword.Should().BeNull(
            because: "caller-supplied password must NOT echo via OwnerSeedResult.GeneratedPassword");

        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT password_hash FROM users WHERE email = @e;";
        cmd.Parameters.AddWithValue("e", email);
        var phcHash = (string)(await cmd.ExecuteScalarAsync())!;

        var hasher = BuildHasher();
        hasher.Verify(explicitPwd, phcHash)
            .Should()
            .BeTrue(because: "the explicit password the caller passed must round-trip");
        hasher.Verify("wrong-password", phcHash)
            .Should()
            .BeFalse(because: "the hash is bound to the explicit password, not the wrong one");
    }
}
