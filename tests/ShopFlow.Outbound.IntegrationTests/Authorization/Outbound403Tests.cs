using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using ShopFlow.SharedKernel.Authorization;
using Xunit;

namespace ShopFlow.Outbound.IntegrationTests.Authorization;

/// <summary>
/// Sprint-10.5 U4 — 403 wire-shape pinning for the 10 Outbound actions
/// Sprint-10 U2 attached per-action <c>[Authorize(Policy = PermissionKeys.X)]</c>
/// to. Each fact submits a JWT whose <c>perm[]</c> omits the action's
/// required key (and includes the other 23 of 24); the per-action policy
/// engine rejects on authorization, ASP.NET Core returns 403.
///
/// <para>Endpoint mapping (verified against the live
/// <c>OrdersController</c>):</para>
/// <list type="bullet">
///   <item><c>POST /api/outbound/orders</c> → omit <c>OutboundOrdersWrite</c></item>
///   <item><c>GET /api/outbound/orders/{id}</c> → omit <c>OutboundOrdersRead</c></item>
///   <item><c>GET /api/outbound/orders</c> → omit <c>OutboundOrdersRead</c></item>
///   <item><c>GET /api/outbound/orders/kpis</c> → omit <c>OutboundOrdersRead</c></item>
///   <item><c>GET /api/outbound/orders/{id}/transitions</c> → omit <c>OutboundOrdersRead</c></item>
///   <item><c>POST /api/outbound/orders/seed</c> (Dev) → omit <c>OutboundOrdersWrite</c></item>
///   <item><c>POST /api/outbound/orders/{id}/confirm-pick</c> → omit <c>OutboundOrdersPickConfirm</c></item>
///   <item><c>POST /api/outbound/orders/{id}/mark-pick-failed</c> → omit <c>OutboundOrdersPickConfirm</c> (Sprint-10 KTD8 maps both)</item>
///   <item><c>POST /api/outbound/orders/{id}/confirm-pack</c> → omit <c>OutboundOrdersPackConfirm</c></item>
///   <item><c>POST /api/outbound/orders/{id}/confirm-ship</c> → omit <c>OutboundOrdersShipConfirm</c></item>
/// </list>
///
/// <para>Skip-marked locally per Sprint-1+ posture.</para>
/// </summary>
[Collection(OutboundAuthorizationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class Outbound403Tests
{
    private const string SkipReason =
        "Sprint-10.5 U4: Docker-backed fixture wired in CI tier; dev machine has no Docker daemon";

    /// <summary>
    /// Placeholder order id used in the route. The 403 fires at the
    /// policy filter — well before the controller body — so the id need
    /// not resolve to a real row.
    /// </summary>
    private static readonly Guid OrderIdStub = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly OutboundAuthorizationFixture _fixture;

    public Outbound403Tests(OutboundAuthorizationFixture fixture)
    {
        _fixture = fixture;
    }

    // ── POST /api/outbound/orders ────────────────────────────────────

    [Fact(Skip = SkipReason)]
    public async Task Create_RejectsJwtMissingOutboundOrdersWrite_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.OutboundOrdersWrite);
        var body = new
        {
            channelExternalOrderId = "ext-1",
            shippingProfile = "standard",
            lines = Array.Empty<object>(),
        };
        var response = await client.PostAsJsonAsync("/api/outbound/orders", body);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── GET /api/outbound/orders/{id} ────────────────────────────────

    [Fact(Skip = SkipReason)]
    public async Task GetById_RejectsJwtMissingOutboundOrdersRead_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.OutboundOrdersRead);
        var response = await client.GetAsync($"/api/outbound/orders/{OrderIdStub}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── GET /api/outbound/orders ─────────────────────────────────────

    [Fact(Skip = SkipReason)]
    public async Task List_RejectsJwtMissingOutboundOrdersRead_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.OutboundOrdersRead);
        var response = await client.GetAsync("/api/outbound/orders");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── GET /api/outbound/orders/kpis ────────────────────────────────

    [Fact(Skip = SkipReason)]
    public async Task GetKpis_RejectsJwtMissingOutboundOrdersRead_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.OutboundOrdersRead);
        var response = await client.GetAsync("/api/outbound/orders/kpis");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── GET /api/outbound/orders/{id}/transitions ────────────────────

    [Fact(Skip = SkipReason)]
    public async Task GetTransitions_RejectsJwtMissingOutboundOrdersRead_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.OutboundOrdersRead);
        var response = await client.GetAsync($"/api/outbound/orders/{OrderIdStub}/transitions");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── POST /api/outbound/orders/seed (DEV-only) ────────────────────

    [Fact(Skip = SkipReason)]
    public async Task Seed_RejectsJwtMissingOutboundOrdersWrite_With403()
    {
        // Fixture sets ASPNETCORE_ENVIRONMENT=Development so the
        // IsDevelopment() guard inside SeedAsync passes through to the
        // policy gate; otherwise the 403 would be masked by 404 +
        // environment_not_dev.
        using var client = BuildClientNarrowedFor(PermissionKeys.OutboundOrdersWrite);
        var body = new { lineCount = 1 };
        var response = await client.PostAsJsonAsync("/api/outbound/orders/seed", body);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── POST /api/outbound/orders/{id}/confirm-pick ──────────────────

    [Fact(Skip = SkipReason)]
    public async Task ConfirmPick_RejectsJwtMissingOutboundOrdersPickConfirm_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.OutboundOrdersPickConfirm);
        var response = await client.PostAsync(
            $"/api/outbound/orders/{OrderIdStub}/confirm-pick",
            content: null
        );
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── POST /api/outbound/orders/{id}/mark-pick-failed ──────────────

    [Fact(Skip = SkipReason)]
    public async Task MarkPickFailed_RejectsJwtMissingOutboundOrdersPickConfirm_With403()
    {
        // Sprint-10 KTD8 — MarkPickFailedAsync intentionally gates on
        // OutboundOrdersPickConfirm (no separate cancel key); the orphan
        // key OutboundOrdersCancel is catalog-only.
        using var client = BuildClientNarrowedFor(PermissionKeys.OutboundOrdersPickConfirm);
        var body = new { reason = "test" };
        var response = await client.PostAsJsonAsync(
            $"/api/outbound/orders/{OrderIdStub}/mark-pick-failed",
            body
        );
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── POST /api/outbound/orders/{id}/confirm-pack ──────────────────

    [Fact(Skip = SkipReason)]
    public async Task ConfirmPack_RejectsJwtMissingOutboundOrdersPackConfirm_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.OutboundOrdersPackConfirm);
        var body = new { actualWeightTotal = 100 };
        var response = await client.PostAsJsonAsync(
            $"/api/outbound/orders/{OrderIdStub}/confirm-pack",
            body
        );
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── POST /api/outbound/orders/{id}/confirm-ship ──────────────────

    [Fact(Skip = SkipReason)]
    public async Task ConfirmShip_RejectsJwtMissingOutboundOrdersShipConfirm_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.OutboundOrdersShipConfirm);
        var response = await client.PostAsync(
            $"/api/outbound/orders/{OrderIdStub}/confirm-ship",
            content: null
        );
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Build an HttpClient whose Authorization header carries a JWT with
    /// every key in <see cref="PermissionKeys.All"/> EXCEPT
    /// <paramref name="omittedKey"/>.
    /// </summary>
    private HttpClient BuildClientNarrowedFor(string omittedKey)
    {
        var includeKeys = PermissionKeys
            .All.Where(k => !string.Equals(k, omittedKey, StringComparison.Ordinal))
            .ToArray();
        var jwt = _fixture.JwtBuilder.Build(
            tenantSlug: OutboundAuthorizationFixture.TenantSlug,
            userId: Guid.NewGuid(),
            includeKeys: includeKeys
        );
        var client = _fixture.HttpClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return client;
    }
}
