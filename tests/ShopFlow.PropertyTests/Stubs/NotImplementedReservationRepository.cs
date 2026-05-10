using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.PropertyTests.Stubs;

/// <summary>
/// Stub <see cref="IReservationRepository"/> that throws
/// <see cref="NotImplementedException"/> from every method. The harness in
/// <c>ReservationLedgerProperties.cs</c> wraps each call and treats the
/// throw as the expected-stub-state in W1; the moment Phase-1 Sprint-1
/// lands the real repository, the property assertions become live without
/// any test changes.
/// </summary>
/// <remarks>
/// Per AGENTS.md §8.55: "the harness IS the spec, assertions are quoted from
/// 01-product-development-plan.md.docx §299". The stub messages name the
/// missing implementation by file so a failure under the real impl points
/// directly at the unimplemented seam.
/// </remarks>
public sealed class NotImplementedReservationRepository : IReservationRepository
{
    public const string StubMessagePrefix =
        "ReservationRepository stub — Phase-1 Sprint-1 (W3) lands this; this stub is the spec, not the implementation.";

    public Task<Result<Guid>> TryReserveAsync(
        Guid tenantId,
        Sku sku,
        int qty,
        Guid orderId,
        CancellationToken cancellationToken
    ) =>
        throw new NotImplementedException(
            $"{StubMessagePrefix} TryReserveAsync — see Tech Design §7.2 conditional INSERT CTE."
        );

    public Task<Reservation?> FindByOrderIdAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken
    ) =>
        throw new NotImplementedException(
            $"{StubMessagePrefix} FindByOrderIdAsync — see Tech Design §7.7 idempotency lookup."
        );

    public Task<int> ReleaseExpiredAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException(
            $"{StubMessagePrefix} ReleaseExpiredAsync — see Tech Design §7.4 expiry worker."
        );

    public Task ConfirmAsync(Guid reservationId, CancellationToken cancellationToken) =>
        throw new NotImplementedException(
            $"{StubMessagePrefix} ConfirmAsync — see Tech Design §7.4 confirmation transaction."
        );
}
