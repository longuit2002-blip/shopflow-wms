using FluentAssertions;
using Microsoft.Extensions.Options;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Infrastructure.Storage;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace ShopFlow.Auth.IntegrationTests.Storage;

/// <summary>
/// Sprint-8 U5 — integration coverage for
/// <see cref="RedisRefreshTokenStore"/> against a real Redis via
/// Testcontainers. Validates the issue → rotate → grace-replay
/// → revoke flow as a whole (the per-operation invariants only make
/// sense in concert).
/// </summary>
[Trait("Category", "Integration")]
public sealed class RedisRefreshTokenStoreTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder().WithImage("redis:7").Build();

    private IConnectionMultiplexer _mux = default!;
    private RedisRefreshTokenStore _store = default!;
    private RefreshTokenOptions _options = default!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        _mux = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
        _options = new RefreshTokenOptions
        {
            RefreshTtlDays = 7,
            RememberMeTtlDays = 30,
            RotationGraceWindowSeconds = 60,
            ConnectionString = _redis.GetConnectionString(),
        };
        _store = new RedisRefreshTokenStore(_mux, Options.Create(_options));
    }

    public async Task DisposeAsync()
    {
        _mux.Dispose();
        await _redis.DisposeAsync();
    }

    private async Task FlushAsync()
    {
        // Each test gets a clean Redis state.
        var server = _mux.GetServer(_mux.GetEndPoints()[0]);
        await server.FlushDatabaseAsync();
    }

    [Fact]
    public async Task IssueAsync_StoresLiveKeyWithRefreshTtl()
    {
        await FlushAsync();
        const string tenant = "t1";
        var userId = Guid.NewGuid();

        var token = await _store.IssueAsync(tenant, userId, rememberMe: false, default);

        token.Should().NotBeNullOrWhiteSpace();
        // The hash key has the URL-safe lowercase-hex shape.
        var db = _mux.GetDatabase();
        var server = _mux.GetServer(_mux.GetEndPoints()[0]);
        var keys = server.Keys(pattern: $"refresh:{tenant}:{userId}:*").ToArray();
        keys.Should().HaveCount(1);

        var ttl = await db.KeyTimeToLiveAsync(keys[0]);
        ttl.Should().BeCloseTo(TimeSpan.FromDays(7), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task IssueAsync_RememberMe_StoresLiveKeyWith30DayTtl()
    {
        await FlushAsync();
        const string tenant = "t1";
        var userId = Guid.NewGuid();

        await _store.IssueAsync(tenant, userId, rememberMe: true, default);

        var db = _mux.GetDatabase();
        var server = _mux.GetServer(_mux.GetEndPoints()[0]);
        var keys = server.Keys(pattern: $"refresh:{tenant}:{userId}:*").ToArray();
        var ttl = await db.KeyTimeToLiveAsync(keys[0]);
        ttl.Should().BeCloseTo(TimeSpan.FromDays(30), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task RotateAsync_LiveToken_ReturnsIssuedAndDeletesOld()
    {
        await FlushAsync();
        const string tenant = "t1";
        var userId = Guid.NewGuid();

        var original = await _store.IssueAsync(tenant, userId, rememberMe: false, default);
        var result = await _store.RotateAsync(tenant, userId, original, default);

        result.Outcome.Should().Be(RefreshRotateOutcome.Issued);
        result.NewToken.Should().NotBeNullOrWhiteSpace();
        result.NewToken.Should().NotBe(original);

        // Old hash no longer live; new hash IS live; tombstone present.
        var server = _mux.GetServer(_mux.GetEndPoints()[0]);
        server.Keys(pattern: $"refresh:{tenant}:{userId}:*").Should().HaveCount(1);
        server.Keys(pattern: $"refresh:rotated:{tenant}:{userId}:*").Should().HaveCount(1);
    }

    [Fact]
    public async Task RotateAsync_ConcurrentRetryWithinGraceWindow_ReturnsSameSuccessor()
    {
        // Legitimate retry pattern: same browser fires the refresh
        // twice (multi-tab race) — both should get the SAME successor.
        await FlushAsync();
        const string tenant = "t1";
        var userId = Guid.NewGuid();

        var original = await _store.IssueAsync(tenant, userId, rememberMe: false, default);
        var first = await _store.RotateAsync(tenant, userId, original, default);
        var second = await _store.RotateAsync(tenant, userId, original, default);

        first.Outcome.Should().Be(RefreshRotateOutcome.Issued);
        second.Outcome.Should().Be(RefreshRotateOutcome.GraceReplay);
        second.NewToken.Should().Be(first.NewToken);
    }

    [Fact]
    public async Task RotateAsync_RememberMeBucketCarriesThroughRotation()
    {
        await FlushAsync();
        const string tenant = "t1";
        var userId = Guid.NewGuid();

        var original = await _store.IssueAsync(tenant, userId, rememberMe: true, default);
        var result = await _store.RotateAsync(tenant, userId, original, default);

        result.Outcome.Should().Be(RefreshRotateOutcome.Issued);
        var server = _mux.GetServer(_mux.GetEndPoints()[0]);
        var newKey = server.Keys(pattern: $"refresh:{tenant}:{userId}:*").Single();
        var db = _mux.GetDatabase();
        var ttl = await db.KeyTimeToLiveAsync(newKey);
        ttl.Should().BeCloseTo(TimeSpan.FromDays(30), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task RotateAsync_UnknownToken_ReturnsNotFound()
    {
        await FlushAsync();
        const string tenant = "t1";
        var userId = Guid.NewGuid();

        var result = await _store.RotateAsync(
            tenant,
            userId,
            "never-issued-token-value-here-32b",
            default
        );

        result.Outcome.Should().Be(RefreshRotateOutcome.NotFound);
        result.NewToken.Should().BeNull();
    }

    [Fact]
    public async Task RevokeAsync_DeletesOnlyThatSession()
    {
        await FlushAsync();
        const string tenant = "t1";
        var userId = Guid.NewGuid();

        var sessionA = await _store.IssueAsync(tenant, userId, rememberMe: false, default);
        var sessionB = await _store.IssueAsync(tenant, userId, rememberMe: false, default);

        await _store.RevokeAsync(tenant, userId, sessionA, default);

        // Session A gone, session B survives → rotation succeeds on B.
        var resA = await _store.RotateAsync(tenant, userId, sessionA, default);
        var resB = await _store.RotateAsync(tenant, userId, sessionB, default);
        resA.Outcome.Should().Be(RefreshRotateOutcome.NotFound);
        resB.Outcome.Should().Be(RefreshRotateOutcome.Issued);
    }

    [Fact]
    public async Task RevokeAllForUserAsync_DeletesEverySessionForThatUserOnly()
    {
        await FlushAsync();
        const string tenant = "t1";
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        var aliceA = await _store.IssueAsync(tenant, alice, rememberMe: false, default);
        var aliceB = await _store.IssueAsync(tenant, alice, rememberMe: false, default);
        var bobOnly = await _store.IssueAsync(tenant, bob, rememberMe: false, default);

        await _store.RevokeAllForUserAsync(tenant, alice, default);

        var resA = await _store.RotateAsync(tenant, alice, aliceA, default);
        var resB = await _store.RotateAsync(tenant, alice, aliceB, default);
        var resBob = await _store.RotateAsync(tenant, bob, bobOnly, default);

        resA.Outcome.Should().Be(RefreshRotateOutcome.NotFound);
        resB.Outcome.Should().Be(RefreshRotateOutcome.NotFound);
        resBob.Outcome.Should().Be(RefreshRotateOutcome.Issued, because: "Bob unaffected");
    }

    [Fact]
    public async Task RevokeAllForUserAsync_DoesNotAffectOtherTenants()
    {
        await FlushAsync();
        var userId = Guid.NewGuid();

        var t1Token = await _store.IssueAsync("t1", userId, rememberMe: false, default);
        var t2Token = await _store.IssueAsync("t2", userId, rememberMe: false, default);

        await _store.RevokeAllForUserAsync("t1", userId, default);

        var t1 = await _store.RotateAsync("t1", userId, t1Token, default);
        var t2 = await _store.RotateAsync("t2", userId, t2Token, default);

        t1.Outcome.Should().Be(RefreshRotateOutcome.NotFound);
        t2.Outcome.Should()
            .Be(RefreshRotateOutcome.Issued, because: "different tenant DB boundary");
    }
}
