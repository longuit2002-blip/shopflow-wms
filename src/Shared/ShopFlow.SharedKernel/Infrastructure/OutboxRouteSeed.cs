namespace ShopFlow.SharedKernel.Infrastructure;

/// <summary>
/// DI-collected seed used by <see cref="OutboxRouteRegistry"/> per Sprint-4
/// plan U4 (K13 close). Per-module composition roots register one of these
/// per type via <c>services.AddOutboxRoute&lt;T&gt;(SendKind.Send, …)</c>;
/// the registry constructor receives the full enumeration at first
/// resolution and writes each row into the in-memory map (last-write-wins).
/// </summary>
public sealed record OutboxRouteSeed(Type MessageType, OutboxRoute Route);
