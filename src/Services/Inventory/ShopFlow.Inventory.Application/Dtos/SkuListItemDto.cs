namespace ShopFlow.Inventory.Application.Dtos;

/// <summary>
/// Row shape for <c>GET /api/v1/inventory/skus</c> (Sprint-6 plan U7).
///
/// Sprint-6 scope: ships fields backed by the existing <c>stock_items</c>
/// schema (sku, available, reserved). Cosmetic fields (name, category,
/// threshold, isFlashSale) are stubbed server-side as nullable for FE
/// rendering and become real columns in Sprint-7 alongside the role +
/// permission expansion.
/// </summary>
/// <remarks>
/// Channel allocations + p24 (24-hour outbound) require cross-module
/// joins (Channel + Outbound databases) and are also stubbed in Sprint-6.
/// Real allocation aggregation lands in Sprint-7 alongside the channel
/// integration screen.
/// </remarks>
public sealed record SkuListItemDto(
    string Sku,
    int Available,
    int Reserved,
    string? Name,
    string? Category,
    int? Threshold,
    bool IsFlashSale,
    IReadOnlyList<ChannelAllocationDto> Allocations,
    int P24Outbound
);

public sealed record ChannelAllocationDto(string Channel, int Allocated);

public sealed record PaginatedSkuListDto(
    IReadOnlyList<SkuListItemDto> Items,
    int Page,
    int PageSize,
    int Total
);
