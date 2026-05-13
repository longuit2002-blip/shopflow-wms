using System.Text.Json;

namespace ShopFlow.SharedKernel.Infrastructure;

/// <summary>
/// Single source of truth for the JSON serialization options used on
/// every outbox payload — write side (<see cref="OutboxInterceptor"/>,
/// per-module outbox writers like <c>InboundOutbox</c>,
/// <c>ReservationRepository.AppendOutbox</c>) and read side
/// (<see cref="MultiplexedOutboxDispatcher{TContext}"/>'s payload
/// deserialization).
/// </summary>
/// <remarks>
/// Sprint-2.5 discovered that the write side used camelCase property
/// naming while the dispatcher's deserialize call used default options
/// (case-sensitive PascalCase). Cross-module consumers received payloads
/// with all properties at their default values — silent data corruption
/// at the boundary. Centralising the options here prevents the drift.
/// See <c>docs/solutions/2026-05-13-cross-module-outbox-table-name-collision.md</c>
/// for the context (the bug surfaced while writing the cross-module
/// flow test that the outbox-table rename unblocked).
/// </remarks>
public static class OutboxJsonOptions
{
    /// <summary>
    /// Canonical options: <c>PropertyNamingPolicy.CamelCase</c> on
    /// serialize, <c>PropertyNameCaseInsensitive = true</c> on
    /// deserialize so older PascalCase-keyed payloads (none exist yet
    /// but the safety is cheap) still round-trip cleanly.
    /// </summary>
    public static JsonSerializerOptions Default { get; } = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
}
