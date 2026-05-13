using ShopFlow.Inventory.Application;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.PropertyTests.Stubs;

/// <summary>
/// Stub adapter that forwards every <see cref="IReservationRepository"/>
/// call to <see cref="ReservationRepositoryHandle.Current"/> when set,
/// or throws <see cref="NotImplementedException"/> otherwise. The
/// property suite instantiates this directly; the fixture sets the
/// handle to a real <c>ReservationRepository</c> before any test runs,
/// so the same property bodies that documented the spec while the
/// implementation was stubbed continue to drive the spec assertions
/// against the live impl.
/// </summary>
public sealed class NotImplementedReservationRepository : IReservationRepository
{
    public const string StubMessagePrefix = "Sprint-1-redux behavior";

    private static IReservationRepository Live =>
        ReservationRepositoryHandle.Current
        ?? throw new NotImplementedException(
            StubMessagePrefix
                + " — install a real ReservationRepository into ReservationRepositoryHandle.Current."
        );

    public Task<Result<Reservation>> TryReserveAsync(
        Sku sku,
        string orderId,
        Quantity quantity,
        TimeSpan ttl,
        CancellationToken ct
    ) => Live.TryReserveAsync(sku, orderId, quantity, ttl, ct);

    public Task<TryReserveLinesResult> TryReserveLinesAsync(
        string orderId,
        IReadOnlyList<LineReservation> lines,
        TimeSpan ttl,
        CancellationToken ct
    ) => Live.TryReserveLinesAsync(orderId, lines, ttl, ct);

    public Task<Reservation?> FindByOrderIdAsync(string orderId, CancellationToken ct) =>
        Live.FindByOrderIdAsync(orderId, ct);

    public Task<Result> ConfirmAsync(string orderId, CancellationToken ct) =>
        Live.ConfirmAsync(orderId, ct);

    public Task<Result> ReleaseAsync(string orderId, CancellationToken ct) =>
        Live.ReleaseAsync(orderId, ct);

    public Task<ReleaseLinesResult> ReleaseLinesAsync(
        string orderId,
        IReadOnlyList<string> orderLineIds,
        CancellationToken ct
    ) => Live.ReleaseLinesAsync(orderId, orderLineIds, ct);

    public Task<int> ReleaseExpiredAsync(DateTime now, int batchSize, CancellationToken ct) =>
        Live.ReleaseExpiredAsync(now, batchSize, ct);
}
