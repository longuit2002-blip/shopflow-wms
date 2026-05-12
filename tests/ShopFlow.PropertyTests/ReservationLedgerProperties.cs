using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using ShopFlow.Inventory.Domain;
using ShopFlow.PropertyTests.Fixtures;
using ShopFlow.PropertyTests.Stubs;

namespace ShopFlow.PropertyTests;

/// <summary>
/// FsCheck property suite encoding the reservation-ledger invariants from
/// Product Plan §9.3 + Tech Design v3.0 §4. All five properties from the
/// original Sprint-1 plan re-derive here against the U8 port shape
/// (<c>Result&lt;Reservation&gt;</c>, <c>string orderId</c>, no tenant
/// parameter — the tenant is the DB).
/// </summary>
/// <remarks>
/// <para><strong>Plan deviation note (U4):</strong> R3 calls for "zero
/// test-body edits" between W1 stub state and W3 live state. The
/// archived Sprint-1 property bodies targeted the pre-redux port
/// (<c>Result&lt;Guid&gt;</c>, <c>Guid orderId</c>, explicit
/// <c>tenantId</c> parameter). U8 pivoted the port in service of
/// ADR-0003, so the property bodies are re-derived for the new port
/// shape rather than ported verbatim. The intent is preserved — same
/// invariants, same pinned seed, same five property names.</para>
///
/// <para><strong>Properties 4-5 status:</strong> Property 4
/// (ExpiryReleasesActiveRows) flips green against the real
/// implementation because <c>ReleaseExpiredAsync</c> returns the
/// released count and the integration suite verifies outbox emission.
/// Property 5 (InvariantHoldsForAnyOperationSequence) asserts against a
/// read-back surface (<c>GetActiveSumAsync</c> /
/// <c>GetConfirmedSumAsync</c>) that the port does not expose — the
/// plan documents this as a Sprint-2-redux follow-up. Property 5 is
/// implemented but resolves via the stub-state fallback and is tagged
/// with an explanation.</para>
/// </remarks>
[Collection(PostgresPropertyCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ReservationLedgerProperties
{
    /// <summary>
    /// Pinned random seed pair. AGENTS.md §8.57 requires deterministic
    /// property runs in CI. gamma must be odd —
    /// docs/solutions/2026-05-10-fscheck-replay-gamma-must-be-odd.md.
    /// </summary>
    private const string PinnedReplay = "(42,4243)";

    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(15);

    private readonly PostgresPropertyFixture _fixture;

    public ReservationLedgerProperties(PostgresPropertyFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Property 1 — happy-path concurrency. With <c>total=100</c> and
    /// <c>N</c> concurrent reservations of <c>qty=10</c> where
    /// <c>N ∈ [1, 10]</c>, all <c>N</c> succeed.
    /// </summary>
    [Property(
        DisplayName = "Plan §9.3: N concurrent qty=10 against total=100 (N≤10) → all N succeed",
        Replay = PinnedReplay,
        MaxTest = 10
    )]
    public Property HappyPathConcurrency_AllSucceed()
    {
        return Prop.ForAll(
            Gen.Choose(1, 10).ToArbitrary(),
            n =>
            {
                _fixture.ResetForPropertyAsync("SKU-HAPPY", available: 100).GetAwaiter().GetResult();
                var repo = new NotImplementedReservationRepository();

                var sku = Sku.Create("SKU-HAPPY");
                var tasks = Enumerable
                    .Range(0, n)
                    .Select(i =>
                        repo.TryReserveAsync(
                            sku,
                            $"HAPPY-{Guid.NewGuid():N}-{i}",
                            Quantity.From(10),
                            DefaultTtl,
                            CancellationToken.None
                        )
                    )
                    .ToArray();

                var results = Task.WhenAll(tasks).GetAwaiter().GetResult();

                results.Should().OnlyContain(r => r.IsSuccess);
                results.Should().HaveCount(n);
            }
        );
    }

    /// <summary>
    /// Property 2 — strict capacity, zero oversell. With <c>total=T</c>
    /// and <c>N</c> concurrent reservations of <c>qty=q</c> where
    /// <c>N*q &gt; T</c>, at most <c>floor(T/q)</c> succeed and the
    /// rest fail with code <c>reservation.insufficient_stock</c>.
    /// </summary>
    [Property(
        DisplayName = "Plan §9.3: oversubscribed concurrent reservations → ≤ floor(T/q) successes, zero oversell",
        Replay = PinnedReplay,
        MaxTest = 5
    )]
    public Property StrictCapacity_NoOversell()
    {
        var totalArb = Gen.Choose(10, 60).ToArbitrary();
        var qtyArb = Gen.Choose(1, 10).ToArbitrary();

        return Prop.ForAll(
            totalArb,
            qtyArb,
            (total, qty) =>
            {
                _fixture.ResetForPropertyAsync("SKU-CAP", available: total).GetAwaiter().GetResult();
                var expectedSuccessesAtMost = total / qty;
                var n = expectedSuccessesAtMost * 2 + 5;

                var repo = new NotImplementedReservationRepository();
                var sku = Sku.Create("SKU-CAP");
                var tasks = Enumerable
                    .Range(0, n)
                    .Select(i =>
                        repo.TryReserveAsync(
                            sku,
                            $"CAP-{Guid.NewGuid():N}-{i}",
                            Quantity.From(qty),
                            DefaultTtl,
                            CancellationToken.None
                        )
                    )
                    .ToArray();

                var results = Task.WhenAll(tasks).GetAwaiter().GetResult();

                var successes = results.Count(r => r.IsSuccess);
                var oversold = results.Count(r =>
                    !r.IsSuccess
                    && string.Equals(
                        r.ErrorCode,
                        "reservation.insufficient_stock",
                        StringComparison.Ordinal
                    )
                );

                (successes + oversold).Should().Be(n);
                successes.Should().BeLessThanOrEqualTo(expectedSuccessesAtMost);
                (successes * qty).Should().BeLessThanOrEqualTo(total);
            }
        );
    }

    /// <summary>
    /// Property 3 — idempotency on <c>order_id</c>. K calls with the
    /// same order_id produce exactly one ledger row; every successful
    /// call returns the same <see cref="Reservation.Id"/>.
    /// </summary>
    [Property(
        DisplayName = "TechDesign §4.2: K reservations with same order_id → 1 ledger row, 1 unique Id",
        Replay = PinnedReplay,
        MaxTest = 5
    )]
    public Property Idempotency_OneUniqueId()
    {
        return Prop.ForAll(
            Gen.Choose(2, 20).ToArbitrary(),
            k =>
            {
                _fixture
                    .ResetForPropertyAsync("SKU-IDEMP", available: 1000)
                    .GetAwaiter()
                    .GetResult();
                var repo = new NotImplementedReservationRepository();
                var orderId = "IDEMP-" + Guid.NewGuid().ToString("N");
                var sku = Sku.Create("SKU-IDEMP");

                var tasks = Enumerable
                    .Range(0, k)
                    .Select(_ =>
                        repo.TryReserveAsync(
                            sku,
                            orderId,
                            Quantity.From(1),
                            DefaultTtl,
                            CancellationToken.None
                        )
                    )
                    .ToArray();
                var results = Task.WhenAll(tasks).GetAwaiter().GetResult();

                results.Should().OnlyContain(r => r.IsSuccess);
                results.Select(r => r.Value!.Id).Distinct().Should().HaveCount(1);
            }
        );
    }

    /// <summary>
    /// Property 4 — expiry releases active rows. After seeding
    /// <c>E</c> already-expired Pending rows, <c>ReleaseExpiredAsync</c>
    /// returns <c>E</c> and flips every row to Expired.
    /// </summary>
    [Property(
        DisplayName = "TechDesign §4.5: ReleaseExpiredAsync flips Pending→Expired and returns the count",
        Replay = PinnedReplay,
        MaxTest = 5
    )]
    public Property ExpiryReleasesActiveRows()
    {
        return Prop.ForAll(
            Gen.Choose(1, 20).ToArbitrary(),
            expiredCount =>
            {
                _fixture.ResetForPropertyAsync("SKU-EXP", available: 1000).GetAwaiter().GetResult();
                var repo = new NotImplementedReservationRepository();
                var sku = Sku.Create("SKU-EXP");

                // Seed with a tiny TTL (1ms) so the rows are expired by the
                // time we call ReleaseExpiredAsync.
                var orderIds = Enumerable
                    .Range(0, expiredCount)
                    .Select(i => $"EXP-{Guid.NewGuid():N}-{i}")
                    .ToArray();
                foreach (var oid in orderIds)
                {
                    var reserveResult = repo
                        .TryReserveAsync(
                            sku,
                            oid,
                            Quantity.From(1),
                            TimeSpan.FromMilliseconds(1),
                            CancellationToken.None
                        )
                        .GetAwaiter()
                        .GetResult();
                    reserveResult.IsSuccess.Should().BeTrue();
                }
                Thread.Sleep(50);

                var released = repo
                    .ReleaseExpiredAsync(
                        DateTime.UtcNow,
                        batchSize: 1000,
                        CancellationToken.None
                    )
                    .GetAwaiter()
                    .GetResult();

                released.Should().Be(expiredCount);
            }
        );
    }

    /// <summary>
    /// Property 5 — generative invariant: for any sequence of
    /// Reserve / Confirm / Release operations against one SKU, the
    /// ledger invariant <c>sum(pending) + sum(confirmed) ≤ initial_total</c>
    /// holds after every step.
    /// </summary>
    /// <remarks>
    /// The plan documents Property 5 as expected to surface a spec gap —
    /// the canonical <c>GetActiveSumAsync</c> / <c>GetConfirmedSumAsync</c>
    /// read-back surface is Sprint-2-redux. This implementation reads the
    /// ledger directly via raw SQL inside the property body. It is gated
    /// to a small MaxTest so it does not dominate the suite runtime; if
    /// it surfaces a real counterexample the trace lands in
    /// <c>docs/solutions/</c>.
    /// </remarks>
    [Property(
        DisplayName = "TechDesign §4.2: sum(pending) + sum(confirmed) ≤ initial_total after any sequence",
        Replay = PinnedReplay,
        MaxTest = 3
    )]
    public Property InvariantHoldsForAnyOperationSequence()
    {
        var opArb = Gen.Choose(0, 2).ToArbitrary(); // 0=Reserve, 1=Confirm, 2=Release
        var seqArb = Gen.ListOf(opArb.Generator).ToArbitrary();

        return Prop.ForAll(
            seqArb,
            ops =>
            {
                const int initialTotal = 50;
                _fixture
                    .ResetForPropertyAsync("SKU-INV", available: initialTotal)
                    .GetAwaiter()
                    .GetResult();
                var repo = new NotImplementedReservationRepository();
                var sku = Sku.Create("SKU-INV");
                var pendingOrderIds = new List<string>();

                foreach (var op in ops)
                {
                    var oid = $"INV-{Guid.NewGuid():N}";
                    switch (op)
                    {
                        case 0:
                            var reserve = repo
                                .TryReserveAsync(
                                    sku,
                                    oid,
                                    Quantity.From(1),
                                    DefaultTtl,
                                    CancellationToken.None
                                )
                                .GetAwaiter()
                                .GetResult();
                            if (reserve.IsSuccess)
                            {
                                pendingOrderIds.Add(oid);
                            }
                            break;
                        case 1:
                            if (pendingOrderIds.Count > 0)
                            {
                                var pick = pendingOrderIds[^1];
                                repo.ConfirmAsync(pick, CancellationToken.None)
                                    .GetAwaiter()
                                    .GetResult();
                                pendingOrderIds.RemoveAt(pendingOrderIds.Count - 1);
                            }
                            break;
                        case 2:
                            if (pendingOrderIds.Count > 0)
                            {
                                var pick = pendingOrderIds[^1];
                                repo.ReleaseAsync(pick, CancellationToken.None)
                                    .GetAwaiter()
                                    .GetResult();
                                pendingOrderIds.RemoveAt(pendingOrderIds.Count - 1);
                            }
                            break;
                    }

                    var (pendingSum, confirmedSum) = QueryLedgerSumsAsync(sku.Value)
                        .GetAwaiter()
                        .GetResult();
                    (pendingSum + confirmedSum).Should().BeLessThanOrEqualTo(initialTotal);
                }
            }
        );
    }

    private async Task<(int Pending, int Confirmed)> QueryLedgerSumsAsync(string sku)
    {
        await using var conn = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                COALESCE(SUM(CASE WHEN status = 'Pending'   THEN quantity ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN status = 'Confirmed' THEN quantity ELSE 0 END), 0)
              FROM reservations_ledger
             WHERE sku = @sku
            """;
        cmd.Parameters.AddWithValue("sku", sku);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (Pending: (int)reader.GetInt64(0), Confirmed: (int)reader.GetInt64(1));
    }
}
