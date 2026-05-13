using ShopFlow.Channel.Application.Ports;
using ShopFlow.Channel.Domain.ProductMappings;

namespace ShopFlow.Channel.Infrastructure.Mapping;

/// <summary>
/// Three-tier <see cref="IProductMappingService"/> implementation per
/// Sprint-4 plan R6/U6. Exact (DB lookup) → Fuzzy (in-process Levenshtein
/// over the channel's mapping set) → null. Fuzzy threshold defaults to
/// 0.6 — top-1 candidate above the threshold wins; ties resolve to the
/// first candidate in DB order (FIFO).
/// </summary>
/// <remarks>
/// In-process Levenshtein is intentionally simple — the catalogue size
/// per channel is bounded at MVP (≤ 5k SKUs per Tech Design §1.4
/// scale-tier table). At mid-market scale (≤ 50k) Sprint-5+ swaps in a
/// Postgres <c>pg_trgm</c>-backed query without changing this port shape.
/// </remarks>
public sealed class HybridProductMappingService : IProductMappingService
{
    public const decimal DefaultFuzzyThreshold = 0.6m;

    private readonly IProductMappingRepository _repo;
    private readonly decimal _fuzzyThreshold;

    public HybridProductMappingService(
        IProductMappingRepository repo,
        decimal? fuzzyThreshold = null
    )
    {
        _repo = repo;
        _fuzzyThreshold = fuzzyThreshold ?? DefaultFuzzyThreshold;
    }

    public async Task<ProductMappingResolution?> ResolveAsync(
        Guid channelId,
        string externalSku,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(externalSku))
        {
            return null;
        }

        var skuResult = ExternalSku.Create(externalSku);
        if (!skuResult.IsSuccess)
        {
            return null;
        }
        var sku = skuResult.Value!;

        // Tier 1: exact match.
        var exact = await _repo.FindExactAsync(channelId, sku, ct).ConfigureAwait(false);
        if (exact is not null)
        {
            return new ProductMappingResolution(
                exact.InternalSku,
                exact.Method,
                exact.ConfidenceScore
            );
        }

        // Tier 2: in-process fuzzy match over the channel's known mappings.
        // For catalogue sizes ≤ 5k this stays well within sub-100ms p99.
        var candidates = await _repo
            .ReadAllByChannelAsync(channelId, ct)
            .ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return null;
        }

        var bestScore = 0m;
        ProductMapping? best = null;
        var needle = sku.Value;
        foreach (var candidate in candidates)
        {
            var score = SimilarityScore(needle, candidate.ExternalSku.Value);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        if (best is null || bestScore < _fuzzyThreshold)
        {
            return null;
        }

        return new ProductMappingResolution(
            best.InternalSku,
            MappingMethod.Fuzzy,
            Math.Round(bestScore, 2)
        );
    }

    /// <summary>
    /// 1 - (Levenshtein distance / max length). Case-insensitive ordinal —
    /// marketplace SKUs round-trip through case-folding tools so the
    /// canonical comparison ignores case.
    /// </summary>
    public static decimal SimilarityScore(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
        {
            return 0m;
        }
        var lhs = a.ToLowerInvariant();
        var rhs = b.ToLowerInvariant();
        if (string.Equals(lhs, rhs, StringComparison.Ordinal))
        {
            return 1m;
        }
        var distance = Levenshtein(lhs, rhs);
        var maxLen = Math.Max(lhs.Length, rhs.Length);
        return 1m - ((decimal)distance / maxLen);
    }

    /// <summary>
    /// Standard Levenshtein edit distance — iterative two-row variant so
    /// the allocation cost stays at O(min(|a|, |b|)) regardless of input
    /// length.
    /// </summary>
    public static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        // Make the inner loop the shorter string to bound allocation.
        if (a.Length > b.Length)
        {
            (a, b) = (b, a);
        }

        Span<int> prev = stackalloc int[a.Length + 1];
        Span<int> curr = stackalloc int[a.Length + 1];

        for (var i = 0; i <= a.Length; i++)
        {
            prev[i] = i;
        }

        for (var j = 1; j <= b.Length; j++)
        {
            curr[0] = j;
            for (var i = 1; i <= a.Length; i++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[i] = Math.Min(
                    Math.Min(curr[i - 1] + 1, prev[i] + 1),
                    prev[i - 1] + cost
                );
            }
            prev.Clear();
            curr.CopyTo(prev);
        }

        return prev[a.Length];
    }
}
