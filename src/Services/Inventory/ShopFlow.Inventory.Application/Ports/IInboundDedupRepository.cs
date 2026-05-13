namespace ShopFlow.Inventory.Application.Ports;

/// <summary>
/// Cross-module idempotency anchor per Sprint-2-redux plan R11. The
/// Inventory consumer INSERTs a row keyed on <c>(receiving_id, line_id)</c>
/// inside its transaction; a duplicate redelivery trips <c>23505</c>
/// which the consumer catches and treats as "already processed".
/// </summary>
public interface IInboundDedupRepository
{
    /// <summary>
    /// Attempt to record this receiving-line as processed. Returns
    /// <c>true</c> if the row was new (caller should proceed with the
    /// stock change); returns <c>false</c> if the row already existed
    /// (caller should ACK without further writes — duplicate delivery).
    /// </summary>
    Task<bool> TryRecordAsync(
        Guid receivingId,
        Guid lineId,
        string sku,
        int quantity,
        DateTime processedAt,
        CancellationToken ct
    );
}
