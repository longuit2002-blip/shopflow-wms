using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.Application.Ports;

/// <summary>
/// Read-only surface for the <see cref="Picker"/> reference-data table
/// per Sprint-3-redux U5. The wave generator pulls the picker pool once
/// per tenant tick + uses a round-robin cursor to assign each wave.
/// </summary>
/// <remarks>
/// Tenant id is implicit via the per-tenant database — no
/// <c>tenant_id</c> parameter. Returned order is stable (PK
/// <c>picker_id</c> ascending) so the round-robin cursor is
/// deterministic across ticks per the plan U5 test scenario 4.
/// </remarks>
public interface IPickerRepository
{
    Task<IReadOnlyList<Picker>> ListByTenantAsync(CancellationToken ct);
}
