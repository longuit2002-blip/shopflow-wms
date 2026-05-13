using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.Application;

/// <summary>
/// Discriminated outcome carrier for
/// <see cref="Ports.IReservationRepository.TryReserveLinesAsync"/> per
/// Sprint-3-redux K11. The existing <see cref="ShopFlow.SharedKernel.Domain.Result{T}"/>
/// carries only a single error string + code; the multi-line all-or-nothing
/// CTE needs to surface per-line PASS/OVERSOLD data so the saga can decide
/// whether (and which) compensation Release-Stock to publish.
/// </summary>
/// <remarks>
/// Picking this over option (b) (a separate non-mutating <c>CheckLinesAsync</c>)
/// per the U3 plan note: one atomic round-trip, no extra read, no
/// time-of-check-to-time-of-use racet between the check and the write.
/// </remarks>
public sealed class TryReserveLinesResult
{
    public bool IsSuccess { get; }

    public IReadOnlyList<Reservation> Reservations { get; }

    public IReadOnlyList<LineOutcome> LineOutcomes { get; }

    public string? Error { get; }

    public string? ErrorCode { get; }

    private TryReserveLinesResult(
        bool isSuccess,
        IReadOnlyList<Reservation> reservations,
        IReadOnlyList<LineOutcome> outcomes,
        string? error,
        string? errorCode
    )
    {
        IsSuccess = isSuccess;
        Reservations = reservations;
        LineOutcomes = outcomes;
        Error = error;
        ErrorCode = errorCode;
    }

    /// <summary>
    /// All N requested lines successfully inserted into the ledger. The
    /// <paramref name="reservations"/> list and the <paramref name="outcomes"/>
    /// list are 1:1 aligned by index; every outcome has
    /// <see cref="LineOutcomeStatus.Reserved"/>.
    /// </summary>
    public static TryReserveLinesResult Success(
        IReadOnlyList<Reservation> reservations,
        IReadOnlyList<LineOutcome> outcomes
    ) => new(true, reservations, outcomes, null, null);

    /// <summary>
    /// Atomic failure: at least one line oversold, the CTE inserted zero
    /// rows, the <paramref name="outcomes"/> list reports per-line
    /// PASS/OVERSOLD so the caller knows which lines individually had
    /// stock available even though the atomic group failed.
    /// </summary>
    public static TryReserveLinesResult Failure(
        string error,
        string code,
        IReadOnlyList<LineOutcome> outcomes
    ) => new(false, Array.Empty<Reservation>(), outcomes, error, code);
}
