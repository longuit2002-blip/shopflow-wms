using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShopFlow.Contracts.Inventory;
using ShopFlow.Inventory.Application;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain.Catalog;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Domain;
using ShopFlow.SharedKernel.Infrastructure;
using SkuCode = ShopFlow.Inventory.Domain.Sku;

namespace ShopFlow.Inventory.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ISkuRepository"/> — Sprint-7.5 U3.
/// Replaces the singleton in-memory <c>InMemorySkuMetadataStore</c> with
/// a per-tenant table-backed catalog. The DB-per-tenant binding flows
/// through the scoped <see cref="InventoryDbContext"/> as with every
/// other Inventory repo (AGENTS.md §3.17).
/// </summary>
/// <remarks>
/// <para>The Sprint-5 <c>SkuFlagRepository</c> path (boolean-only
/// <c>(tenant, sku) -&gt; is_flash_sale</c> cache used by StockSync) is
/// orthogonal and stays untouched. Sprint-7.5 U5 wires the
/// <c>UpdateFlashSaleAsync</c> seam to a <c>SkuFlashSaleChangedV1</c>
/// outbox emit so StockSync's cache invalidates downstream; U3 ships
/// the seam (the <c>(Sku, bool Changed)</c> return) and lets U5 hook
/// in.</para>
/// </remarks>
public sealed class SkuRepository : ISkuRepository
{
    private readonly InventoryDbContext _db;
    private readonly IRequestContext _requestContext;

    public SkuRepository(InventoryDbContext db, IRequestContext requestContext)
    {
        _db = db;
        _requestContext = requestContext;
    }

    public async Task<Sku?> GetByIdAsync(SkuCode code, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(code);
        return await _db
            .Set<Sku>()
            .AsTracking()
            .FirstOrDefaultAsync(s => s.Code == code, ct)
            .ConfigureAwait(false);
    }

    public async Task<SkuMutationResult> UpsertAsync(Sku sku, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sku);

        var existing = await _db
            .Set<Sku>()
            .AsTracking()
            .FirstOrDefaultAsync(s => s.Code == sku.Code, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await _db.Set<Sku>().AddAsync(sku, ct).ConfigureAwait(false);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return new SkuMutationResult(sku, true);
        }

        var updateResult = existing.UpdateMetadata(
            name: sku.Name,
            category: sku.Category,
            threshold: sku.Threshold,
            weightGrams: sku.WeightGrams,
            dimensions: sku.Dimensions,
            description: sku.Description,
            imageUrl: sku.ImageUrl,
            barcode: sku.Barcode,
            brand: sku.Brand
        );

        // Caller is the repository's own UpsertAsync — invalid payloads
        // are domain bugs, not runtime cases. The Result<bool> shape lets
        // higher-level handlers surface user-facing validation errors;
        // here we only care about the changed flag.
        if (!updateResult.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Sku.UpdateMetadata rejected the upsert payload: {updateResult.ErrorCode} — {updateResult.Error}"
            );
        }

        var changed = updateResult.Value;

        // Flash-sale flips do not flow through UpdateMetadata — preserve
        // the existing aggregate's flag unless the caller's payload
        // explicitly toggled it. (Callers use UpdateFlashSaleAsync for
        // that path; UpsertAsync is the metadata write path.)
        if (existing.IsFlashSale != sku.IsFlashSale)
        {
            existing.UpdateFlashSale(sku.IsFlashSale);
            changed = true;
        }

        if (changed)
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return new SkuMutationResult(existing, changed);
    }

    public async Task<(IReadOnlyList<Sku> Items, int Total)> ListPagedAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken ct
    )
    {
        var size = Math.Clamp(pageSize, 1, 200);
        var p = Math.Max(1, page);
        var skip = (p - 1) * size;

        IQueryable<Sku> q = _db.Set<Sku>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(it =>
                EF.Functions.ILike(((string)(object)it.Code), $"%{s}%")
                || EF.Functions.ILike(it.Name, $"%{s}%"));
        }

        var total = await q.CountAsync(ct).ConfigureAwait(false);

        var items = await q
            .OrderBy(it => it.Code)
            .Skip(skip)
            .Take(size)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return (items, total);
    }

    public async Task<Result<SkuMutationResult>> UpdateFlashSaleAsync(
        SkuCode code,
        bool active,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(code);

        var existing = await _db
            .Set<Sku>()
            .AsTracking()
            .FirstOrDefaultAsync(s => s.Code == code, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            // Minimal-row create so the flash-sale toggle does not have
            // to ALSO go through the Create SKU modal. Mirrors the
            // threshold-update auto-create path. When <paramref name="active"/>
            // is false against a never-seen-before SKU the insert is a
            // logical no-op from the caller's perspective; the row
            // shows up in the catalog at <c>is_flash_sale = false</c>,
            // which matches the default state.
            var createResult = Sku.Create(
                code: code,
                name: code.Value,
                isFlashSale: active
            );
            if (!createResult.IsSuccess)
            {
                return Result<SkuMutationResult>.Failure(
                    createResult.Error!,
                    createResult.ErrorCode
                );
            }

            var fresh = createResult.Value!;
            await _db.Set<Sku>().AddAsync(fresh, ct).ConfigureAwait(false);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<SkuMutationResult>.Success(new SkuMutationResult(fresh, true));
        }

        var changed = existing.UpdateFlashSale(active);
        if (changed)
        {
            // Sprint-7.5 U5 — outbox emit on state change (closes Sprint-6
            // trade-off #10). Skips on no-op writes so MT redelivery of an
            // already-applied toggle doesn't produce a second event.
            AppendOutbox(
                new SkuFlashSaleChangedV1(
                    TenantId: _requestContext.TenantId,
                    Sku: existing.Code.Value,
                    IsFlashSale: active,
                    OccurredAt: DateTime.UtcNow
                ),
                DateTime.UtcNow);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Result<SkuMutationResult>.Success(new SkuMutationResult(existing, changed));
    }

    /// <summary>
    /// Sprint-7.5 U5 — outbox append for cross-module integration events.
    /// Mirrors the Sprint-1-redux <c>ReservationRepository.AppendOutbox&lt;T&gt;</c>
    /// shape — same EF DbSet, JSON serialization via
    /// <see cref="OutboxJsonOptions.Default"/>, tenant + trace context
    /// captured at write time, payload written in the same transaction
    /// as the row that triggered it.
    /// </summary>
    private void AppendOutbox<T>(T integrationEvent, DateTime occurredAt) where T : class
    {
        var traceId = Activity.Current?.TraceId.ToString();
        _db.OutboxMessages.Add(
            new OutboxMessage
            {
                Id = Guid.NewGuid(),
                TenantId = _requestContext.TenantId,
                EventType = typeof(T).AssemblyQualifiedName!,
                Payload = JsonSerializer.Serialize(integrationEvent, typeof(T), OutboxJsonOptions.Default),
                TraceId = traceId,
                CreatedAt = occurredAt,
            }
        );
    }

    public async Task<Result<SkuMutationResult>> UpdateThresholdAsync(
        SkuCode code,
        int? threshold,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(code);
        if (threshold is < 0)
        {
            return Result<SkuMutationResult>.Failure(
                "threshold must be >= 0.",
                "sku.threshold_negative"
            );
        }

        var existing = await _db
            .Set<Sku>()
            .AsTracking()
            .FirstOrDefaultAsync(s => s.Code == code, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            // Minimal-row create so the inline threshold-edit path does
            // not have to ALSO go through the Create SKU modal.
            var createResult = Sku.Create(
                code: code,
                name: code.Value,
                threshold: threshold
            );
            if (!createResult.IsSuccess)
            {
                return Result<SkuMutationResult>.Failure(
                    createResult.Error!,
                    createResult.ErrorCode
                );
            }

            var fresh = createResult.Value!;
            await _db.Set<Sku>().AddAsync(fresh, ct).ConfigureAwait(false);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<SkuMutationResult>.Success(new SkuMutationResult(fresh, true));
        }

        var updateResult = existing.UpdateThreshold(threshold);
        if (!updateResult.IsSuccess)
        {
            return Result<SkuMutationResult>.Failure(
                updateResult.Error!,
                updateResult.ErrorCode
            );
        }

        var entry = _db.Entry(existing);
        var changed = entry.State == EntityState.Modified;
        if (changed)
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Result<SkuMutationResult>.Success(new SkuMutationResult(existing, changed));
    }

    public async Task<int?> GetThresholdAsync(SkuCode code, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(code);
        return await _db
            .Set<Sku>()
            .AsNoTracking()
            .Where(s => s.Code == code)
            .Select(s => s.Threshold)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> IsFlashSaleAsync(SkuCode code, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(code);
        return await _db
            .Set<Sku>()
            .AsNoTracking()
            .Where(s => s.Code == code)
            .Select(s => s.IsFlashSale)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, SkuListMetadataDto>> GetListMetadataAsync(
        IReadOnlyCollection<string> skuCodes,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(skuCodes);
        if (skuCodes.Count == 0)
        {
            return new Dictionary<string, SkuListMetadataDto>();
        }

        // EF's value converter for Code means the in-memory key on the
        // entity is a Sku value object. Convert the input strings to
        // SkuCode instances so EF's Contains translation applies the
        // converter to each element naturally (yields SQL IN against
        // the converted [sku] text column).
        var codes = skuCodes
            .Select(SkuCode.Create)
            .ToList();

        var rows = await _db
            .Set<Sku>()
            .AsNoTracking()
            .Where(s => codes.Contains(s.Code))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.ToDictionary(
            r => r.Code.Value,
            r => new SkuListMetadataDto(
                Sku: r.Code.Value,
                Name: r.Name,
                Category: r.Category,
                Threshold: r.Threshold,
                IsFlashSale: r.IsFlashSale));
    }

    public async Task<IReadOnlyDictionary<string, int>> GetAllThresholdsAsync(CancellationToken ct)
    {
        // Two-step projection: read minimal columns to memory first
        // (avoids translator complaints about the value-converter
        // SkuCode field) then build the dictionary client-side.
        var rows = await _db
            .Set<Sku>()
            .AsNoTracking()
            .Where(s => s.Threshold != null)
            .Select(s => new { s.Code, s.Threshold })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.ToDictionary(r => r.Code.Value, r => r.Threshold!.Value);
    }
}
