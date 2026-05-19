using System.Diagnostics;
using ShopFlow.Contracts.Outbound;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.Application.Sagas;

/// <summary>
/// Sprint-7 U2 — single audit-write surface for every <c>FulfillmentSaga</c>
/// state transition. Honors the doc-review architectural decision that the
/// audit hook should catch <em>every</em> TransitionTo uniformly — including
/// the <c>WhenEnter</c> <c>IfElse</c> branches (Path A atomic-fail → Cancelled
/// and Path B counter-drain → Cancelled), the <c>If</c> branch on the
/// StockReleased counter, and the chained <c>StockReserved → Reserved →
/// AwaitingPick</c> compound transition — so that the saga's history is
/// always observable from the Orders detail UI regardless of which branch
/// in the state machine fired.
/// </summary>
/// <remarks>
/// <para><b>Wiring strategy:</b> the observer's <see cref="RecordAsync"/> is
/// invoked explicitly via <c>.ThenAsync(ctx => observer.RecordAsync(...))</c>
/// at every <c>TransitionTo(...)</c> call site in <c>FulfillmentSaga.cs</c>.
/// The class-shape architectural decision (single observer, comprehensive
/// branch coverage) is preserved; the connection mechanism is per-branch
/// rather than MT's <c>IStateObserver&lt;T&gt;</c> because the latter is
/// not reliably exposed through MT 8.3.4's <c>MassTransitStateMachine&lt;T&gt;</c>
/// and backend tests run only in CI on this dev machine (no local iteration
/// surface for verifying MT's observer-connector wiring).</para>
///
/// <para><b>Atomicity:</b> resolved Scoped, this observer shares the saga's
/// consume-scope <see cref="OutboundDbContext"/> with the MT EF saga
/// repository. <see cref="IOrderTransitionRepository.AppendAsync"/> tracks
/// the audit row without flushing; <see cref="IOutboundOutbox.AppendAsync"/>
/// tracks the integration-event row likewise. Both flush when the saga's
/// MT-driven commit fires <c>SaveChangesAsync</c> on that shared DbContext
/// — one transaction, atomic with the saga state row update. If either
/// write throws, the entire saga commit fails and MT redelivers; under
/// at-least-once redelivery the audit row may double-write (no UNIQUE
/// constraint yet — accepted per the Sprint-7 plan risks table).</para>
///
/// <para><b>Envelope fields per AGENTS.md §6.42:</b> the observer captures
/// the wall-clock timestamp from <see cref="TimeProvider"/> at write time,
/// and the W3C TraceContext correlation id from
/// <see cref="Activity.Current"/> (falling back to <see cref="Guid.NewGuid"/>
/// only if no Activity is in scope — which would only happen in tests).</para>
/// </remarks>
public sealed class SagaTransitionObserver
{
    private readonly IOrderTransitionRepository _transitions;
    private readonly IOutboundOutbox _outbox;
    private readonly TimeProvider _clock;

    public SagaTransitionObserver(
        IOrderTransitionRepository transitions,
        IOutboundOutbox outbox,
        TimeProvider clock
    )
    {
        _transitions = transitions;
        _outbox = outbox;
        _clock = clock;
    }

    /// <summary>
    /// Record one state transition. Writes the audit row + appends the
    /// <see cref="SagaTransitionedV1"/> integration event to the outbox.
    /// Both tracked-but-not-flushed; the saga's MT commit flushes both.
    /// </summary>
    /// <param name="orderId">Saga's <c>CorrelationId</c> (= Order aggregate Id per K2).</param>
    /// <param name="tenantId">From <c>FulfillmentSagaState.TenantId</c> (populated by the Initially handler).</param>
    /// <param name="fromState">State the saga is leaving.</param>
    /// <param name="toState">State the saga is entering.</param>
    /// <param name="eventType">CLR-name of the integration event that triggered this transition.</param>
    /// <param name="ct">Cancellation from the MT consume context.</param>
    public async Task RecordAsync(
        Guid orderId,
        Guid tenantId,
        string fromState,
        string toState,
        string eventType,
        CancellationToken ct
    )
    {
        var occurredAt = _clock.GetUtcNow().UtcDateTime;
        var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

        var transition = OrderTransition.Create(
            orderId: orderId,
            fromState: fromState,
            toState: toState,
            occurredAt: occurredAt,
            eventType: eventType,
            correlationId: correlationId
        );

        await _transitions.AppendAsync(transition, ct).ConfigureAwait(false);

        var integrationEvent = new SagaTransitionedV1(
            TenantId: tenantId,
            OrderId: orderId,
            FromState: fromState,
            ToState: toState,
            OccurredAt: occurredAt,
            EventType: eventType,
            CorrelationId: correlationId
        );

        await _outbox
            .AppendAsync(nameof(SagaTransitionedV1), integrationEvent, ct)
            .ConfigureAwait(false);
    }
}
