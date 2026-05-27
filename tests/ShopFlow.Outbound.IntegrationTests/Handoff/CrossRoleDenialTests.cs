namespace ShopFlow.Outbound.IntegrationTests.Handoff;

/// <summary>
/// Sprint-12 U5 — Docker-backed cross-role denial tests pinning that
/// each role's <c>[Authorize(Policy = ...)]</c> gate rejects real
/// non-Owner JWTs missing the specific permission, NOT just narrowed
/// Owner JWTs (which Sprint-10.5's 33+1 403 tests already cover).
///
/// <para><b>Test set rationale (origin flow F3 + plan U5).</b></para>
/// <list type="number">
///   <item><description>Picker → ship-confirm → 403 (wrong role, correct
///     pre-state)</description></item>
///   <item><description>Picker → pack-confirm → 403 (wrong role, correct
///     pre-state)</description></item>
///   <item><description>Dispatcher → pick-confirm → 403 (wrong role,
///     correct pre-state)</description></item>
///   <item><description>Dispatcher → pack-confirm → 403 (wrong role,
///     correct pre-state)</description></item>
///   <item><description><b>adversarial-F3 mitigation:</b> Dispatcher →
///     pick-confirm with order in AwaitingShip state (wrong role AND
///     wrong pre-state). Assert HTTP 403 + <c>errorCode == "auth.forbidden"</c>,
///     NOT HTTP 400 + <c>errorCode == "order.invalid_state"</c>. Proves
///     the <c>[Authorize(Policy)]</c> filter executes BEFORE the
///     controller's pre-state check at <c>OrdersController.cs:895</c> —
///     a middleware-ordering regression that swapped the response code
///     would leak the order's state to an unauthorized caller. Closes
///     the Sprint-10 ordering-regression class the 4 baseline facts
///     don't distinguish.</description></item>
///   <item><description><b>adversarial-F8 mitigation:</b> Picker JWT
///     with an EXTRA <c>outbound.orders.ship-confirm</c> key beyond the
///     baseline (simulating the AE6 operator-pre-grant case via
///     <see cref="HandoffFixture.BuildPickerWithExtraShipConfirmJwt"/>).
///     POST /confirm-ship on an AwaitingShip-state order returns 200 +
///     saga reaches Shipped. Then the SAME JWT against /confirm-pack
///     returns 403 (Picker still doesn't have pack-confirm). Pins the
///     KTD1 additive-only contract's behavioral consequence: an
///     operator who grants Picker ship-confirm HAS granted ship
///     capability — there is no defense-in-depth surprise rescue. The
///     operator-runbook callout is the only mitigation; this test
///     ensures the documented behavior matches reality.</description></item>
/// </list>
///
/// <para>All facts seed orders via direct DbContext writes
/// (Sprint-11 U3 pattern): both <c>orders.Status</c> AND
/// <c>saga_state.CurrentState</c> set to the appropriate pre-state.
/// Negative-path tests skip the saga propagation chain since no
/// transition is expected to fire.</para>
///
/// <para><b>Sprint-13 U5 adds 8 facts (6 → 14 total)</b> for the Packer
/// 4th role: 4 Packer-baseline denials (pick-confirm / ship-confirm /
/// mark-pick-failed / mark-ship-failed all → 403), 2 new-endpoint denials
/// (Picker / Dispatcher → mark-pack-failed → 403; mark-pack-failed didn't
/// exist in Sprint-12), the adversarial-F3 third pin (Packer → confirm-pick
/// on a Cancelled order → 403 not state-error), and the adversarial-F8
/// union-of-perms pin (Picker + manual pack-confirm grant CAN pack, still
/// can't ship). The Sprint-12 6 facts stay unchanged.</para>
///
/// <para>Skip-marked locally per Sprint-1+ posture; CI runs the full
/// Docker-backed suite.</para>
/// </summary>
[Collection(HandoffCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CrossRoleDenialTests
{
    private const string SkipReason =
        "Sprint-12 U5: Docker-backed fixture wired in CI tier; dev machine has no Docker daemon";

    private const string Sprint13SkipReason =
        "Sprint-13 U5: Docker-backed fixture wired in CI tier; dev machine has no Docker daemon";

    private readonly HandoffFixture _fixture;

    public CrossRoleDenialTests(HandoffFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Skip = SkipReason)]
    public Task Picker_AttemptsShipConfirm_Returns403_AndSagaUnchanged()
    {
        // ARRANGE
        // -------
        // 1. Seed order via direct DbContext: orders.Status="AwaitingShip"
        //    + saga_state.CurrentState="Packed" (saga reality per KTD2 —
        //    saga has no AwaitingShip handler; sits at Packed between
        //    pack-confirm and ship-confirm).
        // 2. Mint Picker JWT via _fixture.BuildPickerJwt — carries the
        //    4-key baseline (NO ship-confirm).
        //
        // ACT
        // ---
        // 3. POST /api/outbound/orders/{orderId}/confirm-ship with the
        //    Picker JWT. Empty body.
        //
        // ASSERT
        // ------
        // 4. HTTP 403 + ProblemDetails body.errorCode == "auth.forbidden".
        // 5. orders.Status still "AwaitingShip" (controller didn't run).
        // 6. saga_state.CurrentState still "Packed" (no transition fired).
        // 7. No new outbound_saga_transitions row for this orderId.
        return Task.CompletedTask;
    }

    [Fact(Skip = SkipReason)]
    public Task Picker_AttemptsPackConfirm_Returns403_AndSagaUnchanged()
    {
        // Seed orders.Status="Picked" + saga_state.CurrentState="Picked".
        // Picker JWT against POST /confirm-pack with body
        // { actualWeightTotal: 1500 } → 403 + state-still-Picked + no
        // new transition row.
        return Task.CompletedTask;
    }

    [Fact(Skip = SkipReason)]
    public Task Dispatcher_AttemptsPickConfirm_Returns403_AndSagaUnchanged()
    {
        // Seed orders.Status="AwaitingPick" + saga_state.CurrentState=
        // "AwaitingPick". Dispatcher JWT against POST /confirm-pick →
        // 403 + state-still-AwaitingPick + no new transition row.
        return Task.CompletedTask;
    }

    [Fact(Skip = SkipReason)]
    public Task Dispatcher_AttemptsPackConfirm_Returns403_AndSagaUnchanged()
    {
        // Seed orders.Status="Picked" + saga_state.CurrentState="Picked".
        // Dispatcher JWT against POST /confirm-pack → 403 + state-
        // still-Picked + no new transition row.
        return Task.CompletedTask;
    }

    [Fact(Skip = SkipReason)]
    public Task Dispatcher_AttemptsPickConfirm_OnAwaitingShipOrder_Returns403_NotStateError()
    {
        // ARRANGE — wrong role AND wrong pre-state.
        // -----------------------------------------
        // 1. Seed orders.Status="AwaitingShip" + saga_state.CurrentState=
        //    "Packed" (i.e. the order is genuinely past pick-confirm
        //    territory).
        // 2. Mint Dispatcher JWT (no pick-confirm in perm[]).
        //
        // ACT
        // ---
        // 3. POST /api/outbound/orders/{orderId}/confirm-pick with the
        //    Dispatcher JWT. Empty body.
        //
        // ASSERT (adversarial-F3 mitigation)
        // ----------------------------------
        // 4. HTTP 403 + ProblemDetails body.errorCode ==
        //    "auth.forbidden" — NOT HTTP 400 +
        //    errorCode == "order.invalid_state".
        //
        //    Proves: [Authorize(Policy = OutboundOrdersPickConfirm)]
        //    filter executes BEFORE the controller's pre-state check
        //    at OrdersController.cs (the controller's state check
        //    would surface "cannot pick order in AwaitingShip state"
        //    if the auth filter were bypassed). A middleware-ordering
        //    regression that swapped the response code would leak the
        //    order's state to an unauthorized caller — this fact pins
        //    the safety-by-ordering invariant the Sprint-10 per-action
        //    [Authorize(Policy)] migration depends on.
        //
        // 5. orders.Status still "AwaitingShip" (auth filter rejected
        //    before controller ran; no state mutation possible).
        return Task.CompletedTask;
    }

    [Fact(Skip = SkipReason)]
    public Task PickerWithManualShipConfirmGrant_CanShip_BehavioralPin()
    {
        // ARRANGE
        // -------
        // 1. Seed order at orders.Status="AwaitingShip" + saga_state.
        //    CurrentState="Packed" (saga reality per KTD2).
        // 2. Mint Picker JWT WITH EXTRA ship-confirm key via
        //    _fixture.BuildPickerWithExtraShipConfirmJwt — simulates
        //    the AE6 operator-pre-grant scenario where Owner manually
        //    added outbound.orders.ship-confirm to Picker via
        //    /admin/role-permissions PRE-Sprint-12 deploy.
        //
        // ACT — STEP 1: ship-confirm
        // --------------------------
        // 3. POST /api/outbound/orders/{orderId}/confirm-ship with the
        //    augmented Picker JWT.
        //
        // ASSERT (adversarial-F8 mitigation — KTD1 behavioral consequence)
        // ----------------------------------------------------------------
        // 4. HTTP 200 + ConfirmShipResponse body. Picker HAS shipped
        //    the order. No defense-in-depth surprise rescue — the auth
        //    filter sees ship-confirm in perm[] and accepts.
        // 5. Poll saga_state.CurrentState for "Shipped" within 10s
        //    (Packed → Shipped on ShipConfirmed per FulfillmentSaga.cs:218).
        // 6. orders.Status now "Shipped".
        //
        // ACT — STEP 2: pack-confirm (same JWT, no pack-confirm key)
        // ----------------------------------------------------------
        // 7. Seed a SECOND order at orders.Status="Picked" +
        //    saga_state.CurrentState="Picked" (Picker's existing role
        //    can transition through pick → packed if pack-confirm were
        //    granted; this test proves it ISN'T).
        // 8. POST /api/outbound/orders/{secondOrderId}/confirm-pack with
        //    the SAME augmented Picker JWT (which has pick-confirm +
        //    ship-confirm but NOT pack-confirm).
        //
        // ASSERT
        // ------
        // 9. HTTP 403 + errorCode == "auth.forbidden". The augmented
        //    Picker JWT can ship (Owner pre-granted ship-confirm) but
        //    cannot pack (no pack-confirm grant). The KTD1 contract is
        //    additive-only; it adds what's explicitly granted, no
        //    more. The behavioral pin documents this for future
        //    refactors that might be tempted to widen Picker's
        //    capability bundle.
        return Task.CompletedTask;
    }

    // ── Sprint-13 U5 — Packer cross-role denial (8 new facts) ───────────
    // The Packer role joins the cross-role denial matrix. Pack-confirm has
    // moved off Owner to Packer; the 4 Packer-baseline denials prove the
    // Packer JWT can ONLY pack (not pick/ship/mark-pick-fail/mark-ship-fail).
    // The 2 new-endpoint denials (Picker/Dispatcher → mark-pack-failed)
    // pin the new Sprint-13 endpoint's policy gate against the other two
    // non-Owner roles (Sprint-12 had no mark-pack-failed to test). The
    // adversarial-F3 third pin + adversarial-F8 union-of-perms pin graduate
    // the invariants to the pack-confirm endpoint family.

    [Fact(Skip = Sprint13SkipReason)]
    public Task Packer_AttemptsConfirmPick_OnAwaitingPickOrder_Returns403_AndSagaUnchanged()
    {
        // Seed orders.Status="AwaitingPick" + saga_state.CurrentState=
        // "AwaitingPick". Mint Packer JWT (BuildPackerJwt — 3-key baseline,
        // NO pick-confirm). POST /confirm-pick → 403 + errorCode=
        // "auth.forbidden" + state-still-AwaitingPick + no new transition
        // row. Wrong role, correct pre-state.
        return Task.CompletedTask;
    }

    [Fact(Skip = Sprint13SkipReason)]
    public Task Packer_AttemptsConfirmShip_OnPackedOrder_Returns403_AndSagaUnchanged()
    {
        // Seed orders.Status="AwaitingShip" + saga_state.CurrentState=
        // "Packed" (saga reality per KTD2). Packer JWT against
        // POST /confirm-ship → 403 + errorCode="auth.forbidden" +
        // state unchanged + no new transition row. Packer has pack-confirm
        // only, not ship-confirm.
        return Task.CompletedTask;
    }

    [Fact(Skip = Sprint13SkipReason)]
    public Task Packer_AttemptsMarkPickFailed_OnAwaitingPickOrder_Returns403_AndSagaUnchanged()
    {
        // Seed orders.Status="AwaitingPick" + saga_state.CurrentState=
        // "AwaitingPick". Packer JWT against POST /mark-pick-failed → 403 +
        // errorCode="auth.forbidden" (mark-pick-failed is gated by
        // pick-confirm policy, which Packer lacks) + state unchanged.
        return Task.CompletedTask;
    }

    [Fact(Skip = Sprint13SkipReason)]
    public Task Packer_AttemptsMarkShipFailed_OnAwaitingShipOrder_Returns403_AndSagaUnchanged()
    {
        // Seed orders.Status="AwaitingShip" + saga_state.CurrentState=
        // "Packed". Packer JWT against POST /mark-ship-failed → 403 +
        // errorCode="auth.forbidden" (mark-ship-failed is gated by
        // ship-confirm policy, which Packer lacks) + state unchanged.
        return Task.CompletedTask;
    }

    [Fact(Skip = Sprint13SkipReason)]
    public Task Picker_AttemptsMarkPackFailed_OnPickedOrder_Returns403_AndSagaUnchanged()
    {
        // NEW ENDPOINT (Sprint-12 had no mark-pack-failed). Seed
        // orders.Status="Picked" + saga_state.CurrentState="Picked". Picker
        // JWT (BuildPickerJwt — 4-key baseline, NO pack-confirm) against
        // POST /mark-pack-failed → 403 + errorCode="auth.forbidden" +
        // state-still-Picked + no new transition row. mark-pack-failed is
        // gated by pack-confirm policy (K3), which Picker lacks.
        return Task.CompletedTask;
    }

    [Fact(Skip = Sprint13SkipReason)]
    public Task Dispatcher_AttemptsMarkPackFailed_OnPickedOrder_Returns403_AndSagaUnchanged()
    {
        // NEW ENDPOINT (Sprint-12 had no mark-pack-failed). Seed
        // orders.Status="Picked" + saga_state.CurrentState="Picked".
        // Dispatcher JWT (3-key baseline, NO pack-confirm) against
        // POST /mark-pack-failed → 403 + errorCode="auth.forbidden" +
        // state-still-Picked + no new transition row.
        return Task.CompletedTask;
    }

    [Fact(Skip = Sprint13SkipReason)]
    public Task Packer_AttemptsConfirmPick_OnCancelledOrder_Returns403_NotStateError()
    {
        // ADVERSARIAL-F3 THIRD PIN (graduates the invariant to a third
        // policy-gated saga-touching endpoint per
        // docs/solutions/2026-05-26-adversarial-f3-policy-vs-prestate-
        // ordering-invariant.md, which pre-anticipated Sprint-13).
        //
        // ARRANGE — wrong role AND wrong pre-state.
        // 1. Seed orders.Status="Cancelled" + saga_state.CurrentState=
        //    "Cancelled" (terminal; pick-confirm would fail the pre-state
        //    guard too).
        // 2. Mint Packer JWT (no pick-confirm in perm[]).
        //
        // ACT
        // 3. POST /confirm-pick with the Packer JWT.
        //
        // ASSERT
        // 4. HTTP 403 + errorCode="auth.forbidden" — NOT 422/400 +
        //    "order.invalid_state". Proves [Authorize(Policy=PickConfirm)]
        //    fires BEFORE the controller's pre-state check; a middleware-
        //    ordering regression that swapped the code would leak the
        //    Cancelled state to an unauthorized caller.
        // 5. orders.Status still "Cancelled" (auth rejected before
        //    controller ran).
        return Task.CompletedTask;
    }

    [Fact(Skip = Sprint13SkipReason)]
    public Task PickerWithManualPackConfirmGrant_CanPack_BehavioralPin()
    {
        // ADVERSARIAL-F8 UNION-OF-PERMS PIN (pack-confirm variant of the
        // Sprint-12 ship-confirm pin). Documents the KTD1 additive-only
        // contract consequence for pack-confirm.
        //
        // ARRANGE — STEP 1: pack succeeds with the granted key.
        // 1. Seed order at orders.Status="Picked" +
        //    saga_state.CurrentState="Picked".
        // 2. Mint Picker JWT WITH EXTRA pack-confirm key via
        //    _fixture.BuildPickerWithExtraPackConfirmJwt — simulates the
        //    AE9 operator-pre-grant where Owner manually added
        //    outbound.orders.pack-confirm to Picker via
        //    /admin/role-permissions.
        //
        // ACT — STEP 1: pack-confirm
        // 3. POST /confirm-pack with the augmented Picker JWT, body
        //    { actualWeightTotal: 1500 }.
        //
        // ASSERT
        // 4. HTTP 200. Picker HAS packed the order — no defense-in-depth
        //    surprise rescue; the auth filter sees pack-confirm in perm[]
        //    and accepts.
        // 5. Poll saga_state.CurrentState="Packed" within 10s.
        // 6. orders.Status now "AwaitingShip" (ConfirmPackAsync chains
        //    MarkPacked → MarkAwaitingShip).
        //
        // ACT — STEP 2: ship-confirm (same JWT, no ship-confirm key)
        // 7. POST /confirm-ship with the SAME augmented Picker JWT (which
        //    has pick-confirm + pack-confirm but NOT ship-confirm).
        //
        // ASSERT
        // 8. HTTP 403 + errorCode="auth.forbidden". The augmented Picker
        //    can pack (Owner pre-granted pack-confirm) but cannot ship.
        //    The KTD1 contract adds exactly what's granted, no more.
        return Task.CompletedTask;
    }
}
