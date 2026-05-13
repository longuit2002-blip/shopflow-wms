namespace ShopFlow.Outbound.Application.Sagas.Events;

/// <summary>
/// In-process saga-emitted event raised by <c>FulfillmentSaga</c> on the
/// transition to the <c>Cancelled</c> terminal state per Sprint-3-redux U7.
/// Mirrors the in-process control events (<see cref="PickConfirmed"/> /
/// <see cref="PackConfirmed"/> / <see cref="ShipConfirmed"/>): NOT a
/// cross-module contract (no <c>V1</c> suffix; lives in Application, not
/// <c>ShopFlow.Contracts</c>). Carries the correlation id back to the
/// Outbound side so an <c>OrderCancelledConsumer</c> can mark the Order
/// aggregate as Cancelled. The R3 eventual-consistency boundary applies —
/// the saga commit and the Order row's Cancelled write live in separate
/// EF transactions; an operator GET on the order can briefly observe
/// <c>CompensatingReservation</c> until the consumer commits.
/// </summary>
/// <remarks>
/// <para>The saga publishes this via the activity-binder
/// <c>.Publish(ctx =&gt; new OrderCancelled(ctx.Saga.CorrelationId))</c>
/// inside the <c>WhenEnter(Cancelled, ...)</c> activity. MassTransit's
/// in-memory test harness picks it up just like the cross-module events;
/// in production it routes through RabbitMQ back into the Outbound
/// process's receive endpoint, where the consumer is auto-registered via
/// <c>AddConsumers(asm)</c> in <c>AddShopFlowDefaults</c>.</para>
/// </remarks>
public sealed record OrderCancelled(Guid OrderId);
