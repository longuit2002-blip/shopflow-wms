using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Outbound.Domain;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.TestSupport;

namespace ShopFlow.Outbound.IntegrationTests.Handoff;

/// <summary>
/// Sprint-12 U5 / Sprint-13 U5 / finish-line U4 — Docker-backed cross-role
/// denial proofs (AE4). Each fact pins that a role's
/// <c>[Authorize(Policy = ...)]</c> gate rejects a real non-Owner JWT that is
/// missing the specific permission, returning HTTP 403 and leaving the order
/// state untouched. These are minted from the SAME
/// <c>RolePermissionsSeed.{Picker,Dispatcher,Packer}Baseline</c> constants the
/// provisioner writes (via <see cref="HandoffFixture"/>'s JWT builders), so
/// drift between the seeded role permissions and the JWT under test is
/// impossible.
///
/// <para><b>Why HTTP status, not an <c>auth.forbidden</c> error body.</b> The
/// per-action <c>[Authorize(Policy)]</c> filter rejects with the framework's
/// default 403 (empty body) BEFORE the controller action runs — there is no
/// ProblemDetails <c>errorCode</c> on that path. The proof is therefore the
/// 403 status itself, and for the ordering pins the fact that it is 403 (auth)
/// and NOT 400/422 <c>order.invalid_state</c> (the controller's pre-state
/// guard). A 403 on a wrong-state order proves the auth filter fires before
/// the controller can leak the order's state to an unauthorized caller.</para>
///
/// <para><b>Finish-line U4.</b> These bodies were Skip-marked stubs until the
/// Outbound.Api WAF could boot — it never had, because the production
/// composition double-called <c>AddMassTransit</c> and never registered
/// <c>ITenantCatalog</c>. Both are fixed (kernel <c>ConfigureBus</c> hook +
/// <c>AddControlPlane</c> in Program.cs); see
/// docs/solutions/2026-05-27-outbound-api-never-booted-composition-bugs.md.
/// Gated behind <see cref="ProofFactAttribute"/> — run via <c>task proofs</c>
/// (or CI), skipped on a default no-Docker <c>dotnet test</c>.</para>
/// </summary>
[Collection(HandoffCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Category", "Proof")] // finish-line U4 — selectable via `task proofs`
public sealed class CrossRoleDenialTests
{
    private const string OrdersBase = "/api/outbound/orders";

    private readonly HandoffFixture _fixture;

    public CrossRoleDenialTests(HandoffFixture fixture)
    {
        _fixture = fixture;
    }

    // ── Sprint-12 cross-role denial matrix (6 facts) ──────────────────────

    /// <summary>
    /// Picker (no ship-confirm) → POST /confirm-ship → 403. Uses a random
    /// order id to prove the auth gate fires BEFORE the controller looks the
    /// order up — a non-existent id still 403s, so this is the auth path, not
    /// a not-found/state path.
    /// </summary>
    [ProofFact]
    public async Task Picker_AttemptsShipConfirm_Returns403_AndSagaUnchanged()
    {
        var resp = await Post(
            _fixture.BuildPickerJwt(),
            $"{OrdersBase}/{Guid.NewGuid()}/confirm-ship"
        );

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Picker (no pack-confirm) → POST /confirm-pack on a Picked
    /// order → 403; the order stays Picked.</summary>
    [ProofFact]
    public async Task Picker_AttemptsPackConfirm_Returns403_AndSagaUnchanged()
    {
        var orderId = await SeedOrderAsync(OrderStatus.Picked);

        var resp = await PostJson(
            _fixture.BuildPickerJwt(),
            $"{OrdersBase}/{orderId}/confirm-pack",
            new { actualWeightTotal = 100 }
        );

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadStatusAsync(orderId)).Should().Be(OrderStatus.Picked);
    }

    /// <summary>Dispatcher (no pick-confirm) → POST /confirm-pick on an
    /// AwaitingPick order → 403; the order stays AwaitingPick.</summary>
    [ProofFact]
    public async Task Dispatcher_AttemptsPickConfirm_Returns403_AndSagaUnchanged()
    {
        var orderId = await SeedOrderAsync(OrderStatus.AwaitingPick);

        var resp = await Post(
            _fixture.BuildDispatcherJwt(),
            $"{OrdersBase}/{orderId}/confirm-pick"
        );

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadStatusAsync(orderId)).Should().Be(OrderStatus.AwaitingPick);
    }

    /// <summary>Dispatcher (no pack-confirm) → POST /confirm-pack on a Picked
    /// order → 403; the order stays Picked.</summary>
    [ProofFact]
    public async Task Dispatcher_AttemptsPackConfirm_Returns403_AndSagaUnchanged()
    {
        var orderId = await SeedOrderAsync(OrderStatus.Picked);

        var resp = await PostJson(
            _fixture.BuildDispatcherJwt(),
            $"{OrdersBase}/{orderId}/confirm-pack",
            new { actualWeightTotal = 100 }
        );

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadStatusAsync(orderId)).Should().Be(OrderStatus.Picked);
    }

    /// <summary>
    /// adversarial-F3 — Dispatcher → confirm-pick on an AwaitingShip order
    /// (wrong role AND wrong pre-state). Asserts 403 (auth), NOT 400/422
    /// (<c>order.invalid_state</c>): the <c>[Authorize(Policy)]</c> filter
    /// fires before the controller's pre-state guard, so the order's state is
    /// never leaked to the unauthorized caller. The order stays AwaitingShip.
    /// </summary>
    [ProofFact]
    public async Task Dispatcher_AttemptsPickConfirm_OnAwaitingShipOrder_Returns403_NotStateError()
    {
        var orderId = await SeedOrderAsync(OrderStatus.AwaitingShip);

        var resp = await Post(
            _fixture.BuildDispatcherJwt(),
            $"{OrdersBase}/{orderId}/confirm-pick"
        );

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        resp.StatusCode.Should().NotBe(HttpStatusCode.BadRequest);
        resp.StatusCode.Should().NotBe(HttpStatusCode.UnprocessableEntity);
        (await ReadStatusAsync(orderId)).Should().Be(OrderStatus.AwaitingShip);
    }

    /// <summary>
    /// adversarial-F8 — a Picker JWT carrying an operator-granted EXTRA
    /// ship-confirm key CAN ship (200), pinning the KTD1 additive-only
    /// contract: granting the key grants the capability, no defense-in-depth
    /// surprise rescue. The SAME JWT against confirm-pack still 403s (Picker
    /// was never granted pack-confirm). The grant adds exactly what it says,
    /// no more.
    /// </summary>
    [ProofFact]
    public async Task PickerWithManualShipConfirmGrant_CanShip_BehavioralPin()
    {
        var augmentedPickerJwt = _fixture.BuildPickerWithExtraShipConfirmJwt();

        // Granted: ship an AwaitingShip order → 200 + order reaches Shipped
        // (the controller marks + saves synchronously; the zero-flake mock
        // carrier deterministically succeeds).
        var shipOrderId = await SeedOrderAsync(OrderStatus.AwaitingShip);
        var shipResp = await Post(augmentedPickerJwt, $"{OrdersBase}/{shipOrderId}/confirm-ship");
        shipResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadStatusAsync(shipOrderId)).Should().Be(OrderStatus.Shipped);

        // Not granted: the same JWT against confirm-pack → 403 (no pack-confirm).
        var packOrderId = await SeedOrderAsync(OrderStatus.Picked);
        var packResp = await PostJson(
            augmentedPickerJwt,
            $"{OrdersBase}/{packOrderId}/confirm-pack",
            new { actualWeightTotal = 100 }
        );
        packResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadStatusAsync(packOrderId)).Should().Be(OrderStatus.Picked);
    }

    // ── Sprint-13 Packer cross-role denial (8 facts) ──────────────────────

    /// <summary>Packer (pack-confirm only) → confirm-pick on an AwaitingPick
    /// order → 403; order stays AwaitingPick.</summary>
    [ProofFact]
    public async Task Packer_AttemptsConfirmPick_OnAwaitingPickOrder_Returns403_AndSagaUnchanged()
    {
        var orderId = await SeedOrderAsync(OrderStatus.AwaitingPick);

        var resp = await Post(_fixture.BuildPackerJwt(), $"{OrdersBase}/{orderId}/confirm-pick");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadStatusAsync(orderId)).Should().Be(OrderStatus.AwaitingPick);
    }

    /// <summary>Packer (no ship-confirm) → confirm-ship on an AwaitingShip
    /// order → 403; order stays AwaitingShip.</summary>
    [ProofFact]
    public async Task Packer_AttemptsConfirmShip_OnPackedOrder_Returns403_AndSagaUnchanged()
    {
        var orderId = await SeedOrderAsync(OrderStatus.AwaitingShip);

        var resp = await Post(_fixture.BuildPackerJwt(), $"{OrdersBase}/{orderId}/confirm-ship");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadStatusAsync(orderId)).Should().Be(OrderStatus.AwaitingShip);
    }

    /// <summary>Packer (no pick-confirm; mark-pick-failed is gated by the
    /// pick-confirm policy) → mark-pick-failed → 403; order stays
    /// AwaitingPick.</summary>
    [ProofFact]
    public async Task Packer_AttemptsMarkPickFailed_OnAwaitingPickOrder_Returns403_AndSagaUnchanged()
    {
        var orderId = await SeedOrderAsync(OrderStatus.AwaitingPick);

        var resp = await Post(
            _fixture.BuildPackerJwt(),
            $"{OrdersBase}/{orderId}/mark-pick-failed"
        );

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadStatusAsync(orderId)).Should().Be(OrderStatus.AwaitingPick);
    }

    /// <summary>Packer (no ship-confirm; mark-ship-failed is gated by the
    /// ship-confirm policy) → mark-ship-failed → 403; order stays
    /// AwaitingShip.</summary>
    [ProofFact]
    public async Task Packer_AttemptsMarkShipFailed_OnAwaitingShipOrder_Returns403_AndSagaUnchanged()
    {
        var orderId = await SeedOrderAsync(OrderStatus.AwaitingShip);

        var resp = await Post(
            _fixture.BuildPackerJwt(),
            $"{OrdersBase}/{orderId}/mark-ship-failed"
        );

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadStatusAsync(orderId)).Should().Be(OrderStatus.AwaitingShip);
    }

    /// <summary>Picker (no pack-confirm; mark-pack-failed is gated by the
    /// pack-confirm policy, K3) → mark-pack-failed → 403; order stays
    /// Picked. New Sprint-13 endpoint.</summary>
    [ProofFact]
    public async Task Picker_AttemptsMarkPackFailed_OnPickedOrder_Returns403_AndSagaUnchanged()
    {
        var orderId = await SeedOrderAsync(OrderStatus.Picked);

        var resp = await Post(
            _fixture.BuildPickerJwt(),
            $"{OrdersBase}/{orderId}/mark-pack-failed"
        );

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadStatusAsync(orderId)).Should().Be(OrderStatus.Picked);
    }

    /// <summary>Dispatcher (no pack-confirm) → mark-pack-failed on a Picked
    /// order → 403; order stays Picked. New Sprint-13 endpoint.</summary>
    [ProofFact]
    public async Task Dispatcher_AttemptsMarkPackFailed_OnPickedOrder_Returns403_AndSagaUnchanged()
    {
        var orderId = await SeedOrderAsync(OrderStatus.Picked);

        var resp = await Post(
            _fixture.BuildDispatcherJwt(),
            $"{OrdersBase}/{orderId}/mark-pack-failed"
        );

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadStatusAsync(orderId)).Should().Be(OrderStatus.Picked);
    }

    /// <summary>
    /// adversarial-F3 third pin — Packer → confirm-pick on a Cancelled
    /// (terminal) order. Wrong role AND wrong pre-state. Asserts 403 (auth),
    /// NOT 400/422 (<c>order.invalid_state</c>) — the auth filter fires before
    /// the controller's state check, so the Cancelled state never leaks. The
    /// order stays Cancelled.
    /// </summary>
    [ProofFact]
    public async Task Packer_AttemptsConfirmPick_OnCancelledOrder_Returns403_NotStateError()
    {
        var orderId = await SeedOrderAsync(OrderStatus.Cancelled);

        var resp = await Post(_fixture.BuildPackerJwt(), $"{OrdersBase}/{orderId}/confirm-pick");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        resp.StatusCode.Should().NotBe(HttpStatusCode.BadRequest);
        resp.StatusCode.Should().NotBe(HttpStatusCode.UnprocessableEntity);
        (await ReadStatusAsync(orderId)).Should().Be(OrderStatus.Cancelled);
    }

    /// <summary>
    /// adversarial-F8 pack-confirm variant — a Picker JWT carrying an
    /// operator-granted EXTRA pack-confirm key CAN pack (200; order chains
    /// Picked → Packed → AwaitingShip), but the SAME JWT against confirm-ship
    /// still 403s (no ship-confirm grant). Pins the KTD1 additive-only
    /// contract for the pack-confirm endpoint family.
    /// </summary>
    [ProofFact]
    public async Task PickerWithManualPackConfirmGrant_CanPack_BehavioralPin()
    {
        var augmentedPickerJwt = _fixture.BuildPickerWithExtraPackConfirmJwt();

        // Granted: pack a Picked order → 200 + order reaches AwaitingShip
        // (ConfirmPackAsync chains MarkPacked → MarkAwaitingShip).
        var packOrderId = await SeedOrderAsync(OrderStatus.Picked);
        var packResp = await PostJson(
            augmentedPickerJwt,
            $"{OrdersBase}/{packOrderId}/confirm-pack",
            new { actualWeightTotal = 100 }
        );
        packResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadStatusAsync(packOrderId)).Should().Be(OrderStatus.AwaitingShip);

        // Not granted: the same JWT against confirm-ship → 403 (no ship-confirm).
        var shipResp = await Post(augmentedPickerJwt, $"{OrdersBase}/{packOrderId}/confirm-ship");
        shipResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private HttpClient Authed(string jwt)
    {
        var client = _fixture.HttpClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return client;
    }

    private Task<HttpResponseMessage> Post(string jwt, string path) =>
        Authed(jwt).PostAsync(path, content: null);

    private Task<HttpResponseMessage> PostJson(string jwt, string path, object body) =>
        Authed(jwt).PostAsync(path, JsonContent.Create(body));

    /// <summary>
    /// Seed an order directly via the Outbound DbContext (the
    /// <see cref="Order"/> aggregate populates every column + the
    /// BaseEntity-defaulted timestamps), then flip <c>orders.status</c> to the
    /// target pre-state via raw SQL. Mirrors SagaHappyPathTests' direct-DbContext
    /// create + SetOrderStatusAsync. The <c>orders.status</c> column is the
    /// operator-facing state the controller's pre-state guards read (the saga
    /// is the authoritative state for cross-module commands; the two run a step
    /// apart by design — KTD2).
    /// </summary>
    /// <remarks>
    /// Deliberately bypasses <c>POST /orders/seed</c> (and <c>POST /orders</c>):
    /// both return 500 through the WAF because their <c>CreatedAtAction(
    /// nameof(GetByIdAsync), …)</c> references the action's pre-suffix-strip
    /// name while ASP.NET registered it as "GetById" — a real production bug in
    /// the never-booted Outbound.Api, tracked separately from this RBAC proof.
    /// </remarks>
    private async Task<Guid> SeedOrderAsync(OrderStatus status)
    {
        var options = new DbContextOptionsBuilder<OutboundDbContext>()
            .UseNpgsql(_fixture.TenantConnectionString)
            .Options;

        Guid orderId;
        await using (var db = new OutboundDbContext(options))
        {
            var created = Order.Create(
                channelExternalOrderId: $"ext-{Guid.NewGuid():N}",
                shippingProfile: "standard",
                lines: new[] { ("SKU-A", 1, (int?)100) }
            );
            created.IsSuccess.Should().BeTrue(created.Error);
            var order = created.Value!;
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        if (status != OrderStatus.Created)
        {
            await using var conn = new NpgsqlConnection(_fixture.TenantConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE orders SET status = @s WHERE id = @id";
            cmd.Parameters.AddWithValue("s", status.ToString());
            cmd.Parameters.AddWithValue("id", orderId);
            var rows = await cmd.ExecuteNonQueryAsync();
            rows.Should().Be(1);
        }

        return orderId;
    }

    private async Task<OrderStatus> ReadStatusAsync(Guid orderId)
    {
        await using var conn = new NpgsqlConnection(_fixture.TenantConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM orders WHERE id = @id";
        cmd.Parameters.AddWithValue("id", orderId);
        var raw = (string?)await cmd.ExecuteScalarAsync();
        raw.Should().NotBeNull("seeded order must exist");
        return Enum.Parse<OrderStatus>(raw!);
    }
}
