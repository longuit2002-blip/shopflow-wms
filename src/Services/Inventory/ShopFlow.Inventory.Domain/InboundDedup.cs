namespace ShopFlow.Inventory.Domain;

/// <summary>
/// Idempotency-anchor row for the cross-module Inbound → Inventory flow
/// per Sprint-2-redux plan R11. Composite PK <c>(receiving_id, line_id)</c>
/// — the consumer INSERTs this row first inside its transaction; a
/// duplicate redelivery trips a <c>23505</c> UniqueViolation which the
/// consumer catches and treats as "already processed, ACK and move on".
/// </summary>
public sealed class InboundDedup
{
    public Guid ReceivingId { get; private set; }

    public Guid LineId { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public int Quantity { get; private set; }

    public DateTime ProcessedAt { get; private set; }

    private InboundDedup() { }

    public static InboundDedup Record(
        Guid receivingId,
        Guid lineId,
        string sku,
        int quantity,
        DateTime processedAt
    )
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("sku is required.", nameof(sku));
        }
        return new InboundDedup
        {
            ReceivingId = receivingId,
            LineId = lineId,
            Sku = sku.Trim(),
            Quantity = quantity,
            ProcessedAt = processedAt,
        };
    }
}
