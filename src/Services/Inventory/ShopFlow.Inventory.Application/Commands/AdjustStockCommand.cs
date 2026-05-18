using MediatR;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Application.Commands;

/// <summary>
/// Apply a stock delta to one SKU (Sprint-6 plan U8 / R8).
/// </summary>
/// <param name="Sku">Target SKU.</param>
/// <param name="Delta">Signed delta. Positive = receipt; negative = damage / write-off.</param>
/// <param name="Reason">One of the canon <c>StockAdjustmentReason</c> enum names.</param>
/// <param name="Note">Optional free-text note (≤512 chars).</param>
/// <param name="IdempotencyKey">
/// Caller-supplied request key. Sprint-6 logs it for observability but
/// does NOT dedupe server-side (the historical natural dedupe is the
/// stock_adjustments audit table). Sprint-7 adds the dedicated
/// inventory_idempotency_records table with a request-hash check.
/// </param>
public sealed record AdjustStockCommand(
    string Sku,
    int Delta,
    string Reason,
    string? Note,
    string? IdempotencyKey) : IRequest<Result>;
