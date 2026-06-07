using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using ShopFlow.SharedKernel.Authorization;
using Xunit;

namespace ShopFlow.Inbound.IntegrationTests.Authorization;

/// <summary>
/// Sprint-10.5 U4 — 403 wire-shape pinning for the 6 Inbound actions
/// Sprint-10 U3 attached per-action <c>[Authorize(Policy = PermissionKeys.X)]</c>
/// to. Each fact submits a JWT whose <c>perm[]</c> omits the action's
/// required key (and includes the other 23 of 24); the per-action policy
/// engine rejects on authorization, ASP.NET Core returns 403.
///
/// <para>Endpoint mapping (verified against the live
/// <c>PurchaseOrdersController</c> — doc-review flagged PATCH vs POST
/// + the <c>/receive</c> path):</para>
/// <list type="bullet">
///   <item><c>POST /api/inbound/purchase-orders</c> → omit <c>InboundPosWrite</c></item>
///   <item><c>GET /api/inbound/purchase-orders/{id:guid}</c> → omit <c>InboundPosRead</c></item>
///   <item><c>GET /api/inbound/purchase-orders</c> (open list — no <c>/open</c> suffix) → omit <c>InboundPosRead</c></item>
///   <item><c>PATCH /api/inbound/purchase-orders/{id:guid}/open</c> → omit <c>InboundPosWrite</c></item>
///   <item><c>PATCH /api/inbound/purchase-orders/{id:guid}/cancel</c> → omit <c>InboundPosWrite</c></item>
///   <item><c>POST /api/inbound/purchase-orders/{id:guid}/receive</c> (not <c>/receive-line</c>) → omit <c>InboundReceiveConfirm</c></item>
/// </list>
///
/// <para>Skip-marked locally per Sprint-1+ posture.</para>
/// </summary>
[Collection(InboundAuthorizationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class Inbound403Tests
{
    private const string SkipReason =
        "Sprint-10.5 U4: Docker-backed fixture wired in CI tier; dev machine has no Docker daemon";

    private static readonly Guid PoIdStub = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly InboundAuthorizationFixture _fixture;

    public Inbound403Tests(InboundAuthorizationFixture fixture)
    {
        _fixture = fixture;
    }

    // ── POST /api/inbound/purchase-orders ────────────────────────────

    [Fact(Skip = SkipReason)]
    public async Task Create_RejectsJwtMissingInboundPosWrite_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.InboundPosWrite);
        var body = new
        {
            supplierRef = "SUP-1",
            expectedDeliveryAt = (DateTime?)null,
            lines = Array.Empty<object>(),
        };
        var response = await client.PostAsJsonAsync("/api/inbound/purchase-orders", body);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── GET /api/inbound/purchase-orders/{id} ────────────────────────

    [Fact(Skip = SkipReason)]
    public async Task GetById_RejectsJwtMissingInboundPosRead_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.InboundPosRead);
        var response = await client.GetAsync($"/api/inbound/purchase-orders/{PoIdStub}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── GET /api/inbound/purchase-orders ─────────────────────────────

    [Fact(Skip = SkipReason)]
    public async Task ListOpen_RejectsJwtMissingInboundPosRead_With403()
    {
        // Doc-review verification: route is /api/inbound/purchase-orders
        // (the method name is ListOpenAsync but the [HttpGet] attribute
        // has no path argument, so it hits the controller root).
        using var client = BuildClientNarrowedFor(PermissionKeys.InboundPosRead);
        var response = await client.GetAsync("/api/inbound/purchase-orders");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── PATCH /api/inbound/purchase-orders/{id}/open ─────────────────

    [Fact(Skip = SkipReason)]
    public async Task Open_RejectsJwtMissingInboundPosWrite_With403()
    {
        // Doc-review verification: PATCH not POST.
        using var client = BuildClientNarrowedFor(PermissionKeys.InboundPosWrite);
        var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/inbound/purchase-orders/{PoIdStub}/open"
        );
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── PATCH /api/inbound/purchase-orders/{id}/cancel ───────────────

    [Fact(Skip = SkipReason)]
    public async Task Cancel_RejectsJwtMissingInboundPosWrite_With403()
    {
        // Doc-review verification: PATCH not POST.
        using var client = BuildClientNarrowedFor(PermissionKeys.InboundPosWrite);
        var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/inbound/purchase-orders/{PoIdStub}/cancel"
        )
        {
            Content = JsonContent.Create(new { reason = "test" }),
        };
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── POST /api/inbound/purchase-orders/{id}/receive ───────────────

    [Fact(Skip = SkipReason)]
    public async Task ReceiveLine_RejectsJwtMissingInboundReceiveConfirm_With403()
    {
        // Doc-review verification: /receive not /receive-line.
        using var client = BuildClientNarrowedFor(PermissionKeys.InboundReceiveConfirm);
        var body = new
        {
            receivingId = Guid.NewGuid(),
            purchaseOrderLineId = Guid.NewGuid(),
            actualQty = 1,
            suggestedBinId = (Guid?)null,
            actualBinId = (Guid?)null,
        };
        var response = await client.PostAsJsonAsync(
            $"/api/inbound/purchase-orders/{PoIdStub}/receive",
            body
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
            tenantSlug: InboundAuthorizationFixture.TenantSlug,
            userId: Guid.NewGuid(),
            includeKeys: includeKeys
        );
        var client = _fixture.HttpClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return client;
    }
}
