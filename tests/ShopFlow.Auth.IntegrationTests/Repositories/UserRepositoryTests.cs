using FluentAssertions;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.Auth.Infrastructure;
using ShopFlow.Auth.Infrastructure.Repositories;
using Xunit;

namespace ShopFlow.Auth.IntegrationTests.Repositories;

/// <summary>
/// Sprint-8 U3 integration coverage for <see cref="UserRepository"/>
/// against real Postgres via the shared <see cref="AuthTenantFixture"/>.
/// Validates case-insensitive email lookup + the UNIQUE-23505 →
/// EmailInUse Result branch + paged listing ordering — the three
/// invariants the U7/U8 handlers depend on.
/// </summary>
[Collection(AuthTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class UserRepositoryTests : IAsyncLifetime
{
    private const string ValidHash = "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA";

    private readonly AuthTenantFixture _fx;
    private ProvisionedAuthTenant _tenant = default!;

    public UserRepositoryTests(AuthTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("repo");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private UserRepository BuildRepo(AuthDbContext db) => new(db);

    [Fact]
    public async Task GetByEmailAsync_Missing_ReturnsNull()
    {
        await using var db = new AuthDbContext(_tenant.Options);
        var repo = BuildRepo(db);

        var row = await repo.GetByEmailAsync("missing@example.com", CancellationToken.None);

        row.Should().BeNull();
    }

    [Fact]
    public async Task GetByEmailAsync_CaseInsensitive_MatchesRegardlessOfCase()
    {
        // Insert with mixed case; the aggregate factory normalises to
        // lowercase before persistence.
        await using (var db = new AuthDbContext(_tenant.Options))
        {
            var repo = BuildRepo(db);
            var inserted = User.Create("Owner@Example.COM", ValidHash, UserRole.Owner);
            var addResult = await repo.AddAsync(inserted, CancellationToken.None);
            addResult.IsSuccess.Should().BeTrue();
        }

        // Look up via a completely different casing — should still hit
        // the ux_users_email_lower expression index.
        await using (var db = new AuthDbContext(_tenant.Options))
        {
            var repo = BuildRepo(db);
            var found = await repo.GetByEmailAsync("OWNER@example.com", CancellationToken.None);
            found.Should().NotBeNull();
            found!.Email.Should().Be("owner@example.com");
            found.Role.Should().Be(UserRole.Owner);
        }
    }

    [Fact]
    public async Task AddAsync_FreshRow_SucceedsAndIsFindable()
    {
        await using var db = new AuthDbContext(_tenant.Options);
        var repo = BuildRepo(db);
        var user = User.Create("fresh@example.com", ValidHash, UserRole.Picker);

        var result = await repo.AddAsync(user, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var hydrated = await repo.GetByEmailAsync("fresh@example.com", CancellationToken.None);
        hydrated.Should().NotBeNull();
        hydrated!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task AddAsync_DuplicateEmailDifferentCase_ReturnsEmailInUseFailure()
    {
        // Pre-seed in lowercase.
        await using (var db = new AuthDbContext(_tenant.Options))
        {
            var repo = BuildRepo(db);
            var first = User.Create("clash@example.com", ValidHash, UserRole.Owner);
            (await repo.AddAsync(first, CancellationToken.None)).IsSuccess.Should().BeTrue();
        }

        // Second insert in mixed case — factory normalises, the
        // ux_users_email_lower UNIQUE index fires, repo catches 23505.
        await using (var db = new AuthDbContext(_tenant.Options))
        {
            var repo = BuildRepo(db);
            var clashing = User.Create("Clash@Example.COM", ValidHash, UserRole.Picker);
            var result = await repo.AddAsync(clashing, CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be("auth.email_in_use");
        }
    }

    [Fact]
    public async Task UpdateAsync_AfterSetRole_PersistsTheNewRole()
    {
        Guid userId;
        await using (var db = new AuthDbContext(_tenant.Options))
        {
            var repo = BuildRepo(db);
            var user = User.Create("rotate@example.com", ValidHash, UserRole.Owner);
            (await repo.AddAsync(user, CancellationToken.None)).IsSuccess.Should().BeTrue();
            userId = user.Id;
        }

        await using (var db = new AuthDbContext(_tenant.Options))
        {
            var repo = BuildRepo(db);
            var tracked = await repo.GetByIdAsync(userId, CancellationToken.None);
            tracked.Should().NotBeNull();
            tracked!.SetRole(UserRole.Picker);
            await repo.UpdateAsync(tracked, CancellationToken.None);
        }

        await using (var db = new AuthDbContext(_tenant.Options))
        {
            var repo = BuildRepo(db);
            var reloaded = await repo.GetByIdAsync(userId, CancellationToken.None);
            reloaded!.Role.Should().Be(UserRole.Picker);
            reloaded.UpdatedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task ListAsync_FirstPage_ReturnsRowsOrderedByCreatedAtDesc()
    {
        // Insert 3 rows with explicit ordering.
        var emails = new[] { "a@example.com", "b@example.com", "c@example.com" };
        foreach (var email in emails)
        {
            await using var db = new AuthDbContext(_tenant.Options);
            var repo = BuildRepo(db);
            var user = User.Create(email, ValidHash, UserRole.Owner);
            await repo.AddAsync(user, CancellationToken.None);
            // Add a tiny delay so created_at has visible ordering even
            // on machines with millisecond clock resolution.
            await Task.Delay(10);
        }

        await using (var db = new AuthDbContext(_tenant.Options))
        {
            var repo = BuildRepo(db);
            var page1 = await repo.ListAsync(page: 1, pageSize: 2, CancellationToken.None);

            page1.Should().HaveCount(2);
            // Newest first — "c" was inserted last.
            page1[0].Email.Should().Be("c@example.com");
            page1[1].Email.Should().Be("b@example.com");
        }
    }

    [Fact]
    public async Task ListAsync_RejectsNonPositivePage()
    {
        await using var db = new AuthDbContext(_tenant.Options);
        var repo = BuildRepo(db);

        var act = async () => await repo.ListAsync(page: 0, pageSize: 10, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
