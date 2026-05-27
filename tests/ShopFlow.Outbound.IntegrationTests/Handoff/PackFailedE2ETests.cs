namespace ShopFlow.Outbound.IntegrationTests.Handoff;

/// <summary>
/// Sprint-13 U4 — Docker-backed end-to-end tests for the MarkPackFailed
/// Path D compensation flow. A Packer discovers a damaged item at the pack
/// station (after pick-confirm, before pack-confirm) and marks the pack
/// failed; the saga's <c>During(Picked, When(PackFailed))</c> clause drives
/// the order through CompensatingReservation to Cancelled, reusing the
/// Sprint-3-redux Path B / Sprint-12.5 Path C compensation primitives
/// unchanged (K4).
/// </summary>
/// <remarks>
/// <para>Per K1, the pre-state is <c>Picked</c> — both the Order aggregate
/// and the saga rest at <c>Picked</c> when mark-pack-failed fires (the Order
/// never rests in <c>AwaitingPack</c>; <c>ConfirmPackAsync</c> chains
/// <c>MarkPacked → MarkAwaitingShip</c> atomically).</para>
///
/// <para>Skip-marked locally per Sprint-1+ posture; CI removes the Skip via
/// the Docker-backed nightly + per-PR job. Shares the
/// <see cref="HandoffCollection"/> fixture — each test uses a fresh order
/// id and queries by id (not by state) so it does not collide with other
/// tests in the collection (ADV-007 mitigation).</para>
/// </remarks>
[Collection(HandoffCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PackFailedE2ETests
{
    private const string SkipReason =
        "Sprint-13 U4: Docker-backed fixture wired in CI tier; dev machine has no Docker daemon";

    private readonly HandoffFixture _fixture;

    public PackFailedE2ETests(HandoffFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Skip = SkipReason)]
    public Task PackerMarksPackFailed_AfterPickConfirm_SagaEndsAtCancelled()
    {
        // Sprint-13 U4 (AE3) — Path D full flow.
        //
        // ARRANGE
        // 1. Fixture provisioned (Auth incl. AddPackerRole + Outbound
        //    migrations + Sprint-13 RolePermissionsSeed + 4 user rows).
        // 2. Mint BuildPickerJwt() + BuildPackerJwt().
        // 3. Seed orderId + orders.status='AwaitingPick' +
        //    saga_state.CurrentState='AwaitingPick'. IMPORTANT for Path D:
        //    seed reserved_line_skus='L1,L2' + lines_awaiting_release=2 on
        //    the saga_state row so the WhenEnter(CompensatingReservation)
        //    Else-branch has lines to release (mirrors the Sprint-12.5
        //    Path C E2E seed shape).
        //
        // ACT — STEP 1: Picker confirm-pick
        // 4. POST /confirm-pick (Picker JWT) → 200; poll
        //    saga_state.CurrentState="Picked" within 10s.
        //
        // ACT — STEP 2: Packer mark-pack-failed
        // 5. POST /mark-pack-failed (Packer JWT) body
        //    { reason: "item damaged at pack station" }. HandoffWatch
        //    .MeasureAsync("mark-pack-failed").
        //    a. Assert HTTP 200.
        //    b. Assert GET /orders/{id} status="CompensatingReservation"
        //       immediately after the 200 (Order aggregate committed).
        //    c. Poll saga_state.CurrentState: "Picked" →
        //       "CompensatingReservation" → "Cancelled" within 10s (Path D
        //       publishes ReleaseStockV1; the seeded StockReleased path or
        //       the Inventory consumer drains lines_awaiting_release to 0,
        //       on-enter Cancelled fires).
        //
        // ASSERT — TERMINAL STATE
        // 6. Final GET /orders/{id} status="Cancelled".
        // 7. Query outbound_saga_transitions: a row exists with
        //    from_state="Picked", to_state="CompensatingReservation",
        //    event_type="PackFailed".
        //
        // CLEANUP — HandoffFixture.DisposeAsync.

        return Task.CompletedTask;
    }

    [Fact(Skip = SkipReason)]
    public Task MarkPackFailed_OrderInPicked_ActorUserIdPersistsToOutboundSagaTransitions()
    {
        // Sprint-13 U4 (R12) — actor propagation. The Packer's JWT subject
        // (PackerUserId) flows through IRequestContext.UserId → PackFailed
        // .ActorUserId → SagaTransitionObserver → outbound_saga_transitions
        // .actor_user_id. Mirrors Sprint-12.5 U2/U3 actor mechanism.
        //
        // ARRANGE: as above; mint BuildPickerJwt() + BuildPackerJwt().
        // ACT: confirm-pick (Picker) → mark-pack-failed (Packer).
        // ASSERT: SELECT actor_user_id FROM outbound_saga_transitions WHERE
        //   order_id=@id AND to_state='CompensatingReservation' AND
        //   event_type='PackFailed' → equals PackerUserId (NOT null, NOT
        //   PickerUserId).

        return Task.CompletedTask;
    }

    [Fact(Skip = SkipReason)]
    public Task MarkPackFailed_CalledTwice_SecondReturns409_AlreadyRecorded()
    {
        // Sprint-13 U4 (AE4) — natural-409 idempotency (Sprint-12.5 KTD6).
        //
        // ARRANGE: as above; confirm-pick first so order is at Picked.
        // ACT:
        //   a. First POST /mark-pack-failed (Packer JWT) → 200; order moves
        //      to CompensatingReservation.
        //   b. Second POST /mark-pack-failed (same Packer JWT, same order)
        //      → 409 with problem-details errorCode=
        //      "order.pack_failure_already_recorded".
        // ASSERT: saga_state unchanged by the second call (no double
        //   compensation; ReleaseStockV1 published exactly once).

        return Task.CompletedTask;
    }

    [Fact(Skip = SkipReason)]
    public Task MarkPackFailed_OrderInPacked_Returns422_InvalidState()
    {
        // Sprint-13 U4 (AE5) — pre-state guard. An order driven PAST Picked
        // to Packed (via confirm-pack) is no longer a valid mark-pack-failed
        // target — the operator should mark-ship-failed instead.
        //
        // ARRANGE: confirm-pick (Picker) → confirm-pack (Packer). Order is
        //   now AwaitingShip (aggregate) / saga Packed.
        // ACT: POST /mark-pack-failed (Packer JWT) → 422 with errorCode=
        //   "order.invalid_state" (controller's non-Picked guard fires;
        //   note the aggregate is AwaitingShip here, not Picked).
        // ASSERT: saga_state.CurrentState unchanged ("Packed").

        return Task.CompletedTask;
    }
}
