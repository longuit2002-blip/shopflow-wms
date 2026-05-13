using ShopFlow.Inbound.Domain;

namespace ShopFlow.Inbound.UnitTests.Domain;

/// <summary>
/// Sprint-2-redux U2 — PurchaseOrder state machine + line receipt recording.
/// Covers R1 (state transitions), R2 (line shape), and the R8 auto-transition
/// rules (Open → PartiallyReceived → Closed).
/// </summary>
public sealed class PurchaseOrderTests
{
    private static readonly DateTime Now = new(2026, 5, 13, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EtaPlus3 = Now.AddDays(3);

    private static PurchaseOrder NewDraftPo(params (string Sku, int Qty)[] lines) =>
        PurchaseOrder.Create("SUP-1", EtaPlus3, lines).Value!;

    [Fact]
    public void Create_HappyPath_ProducesDraftWithLines()
    {
        var po = NewDraftPo(("SKU-A", 100), ("SKU-B", 50));

        po.Status.Should().Be(PurchaseOrderStatus.Draft);
        po.SupplierRef.Should().Be("SUP-1");
        po.Lines.Should().HaveCount(2);
        po.Lines.Should().OnlyContain(l => l.ReceivedQty == 0);
    }

    [Fact]
    public void Create_EmptyLines_FailsWithCode()
    {
        var result = PurchaseOrder.Create("SUP-1", EtaPlus3, Array.Empty<(string, int)>());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("po.no_lines");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankSupplier_FailsWithCode(string supplier)
    {
        var result = PurchaseOrder.Create(supplier, EtaPlus3, new[] { ("SKU-A", 1) });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("po.supplier_ref_required");
    }

    [Fact]
    public void Create_NonPositiveExpectedQty_FailsWithCode()
    {
        var result = PurchaseOrder.Create("SUP-1", EtaPlus3, new[] { ("SKU-A", 0) });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("po_line.expected_qty_non_positive");
    }

    [Fact]
    public void Open_FromDraft_TransitionsToOpenAndStampsOpenedAt()
    {
        var po = NewDraftPo(("SKU-A", 10));

        var result = po.Open(Now);

        result.IsSuccess.Should().BeTrue();
        po.Status.Should().Be(PurchaseOrderStatus.Open);
        po.OpenedAt.Should().Be(Now);
    }

    [Fact]
    public void Open_FromCancelled_FailsWithInvalidState()
    {
        var po = NewDraftPo(("SKU-A", 10));
        po.Cancel("oops", Now);

        var result = po.Open(Now.AddMinutes(1));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("po.invalid_state");
    }

    [Fact]
    public void Cancel_FromDraft_StampsCancellationFieldsAndTerminates()
    {
        var po = NewDraftPo(("SKU-A", 10));

        var result = po.Cancel("supplier withdrew", Now);

        result.IsSuccess.Should().BeTrue();
        po.Status.Should().Be(PurchaseOrderStatus.Cancelled);
        po.CancelledAt.Should().Be(Now);
        po.CancellationReason.Should().Be("supplier withdrew");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_BlankReason_FailsWithCode(string reason)
    {
        var po = NewDraftPo(("SKU-A", 10));

        var result = po.Cancel(reason, Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("po.cancel_reason_required");
    }

    [Fact]
    public void Close_FromOpenWithUnderReceivedLines_FailsWithCode()
    {
        var po = NewDraftPo(("SKU-A", 10));
        po.Open(Now);

        var result = po.Close(Now.AddMinutes(1));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("po.not_fully_received");
    }

    [Fact]
    public void RecordLineReceipt_FromOpenWithExactMatch_AutoTransitionsToClosed()
    {
        var po = NewDraftPo(("SKU-A", 50));
        po.Open(Now);
        var lineId = po.Lines.Single().Id;

        var result = po.RecordLineReceipt(lineId, 50, Now.AddMinutes(1));

        result.IsSuccess.Should().BeTrue();
        po.Status.Should().Be(PurchaseOrderStatus.Closed);
        po.ClosedAt.Should().NotBeNull();
        po.Lines.Single().ReceivedQty.Should().Be(50);
    }

    [Fact]
    public void RecordLineReceipt_FromOpenWithPartialReceipt_TransitionsToPartiallyReceived()
    {
        var po = NewDraftPo(("SKU-A", 100), ("SKU-B", 50));
        po.Open(Now);
        var lineA = po.Lines.First(l => l.Sku == "SKU-A");

        var result = po.RecordLineReceipt(lineA.Id, 30, Now.AddMinutes(1));

        result.IsSuccess.Should().BeTrue();
        po.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);
        po.ClosedAt.Should().BeNull();
        lineA.ReceivedQty.Should().Be(30);
    }

    [Fact]
    public void RecordLineReceipt_AcrossMultipleSessions_AccumulatesQty()
    {
        var po = NewDraftPo(("SKU-A", 100));
        po.Open(Now);
        var lineId = po.Lines.Single().Id;

        po.RecordLineReceipt(lineId, 40, Now.AddMinutes(1));
        po.RecordLineReceipt(lineId, 30, Now.AddMinutes(2));

        po.Lines.Single().ReceivedQty.Should().Be(70);
        po.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);
    }

    [Fact]
    public void RecordLineReceipt_OverageAllowedAndClosesAtOrAbove()
    {
        var po = NewDraftPo(("SKU-A", 100));
        po.Open(Now);
        var lineId = po.Lines.Single().Id;

        var result = po.RecordLineReceipt(lineId, 110, Now.AddMinutes(1));

        result.IsSuccess.Should().BeTrue();
        po.Status.Should().Be(PurchaseOrderStatus.Closed);
        po.Lines.Single().ReceivedQty.Should().Be(110);
    }

    [Fact]
    public void RecordLineReceipt_OnCancelledPo_FailsWithInvalidState()
    {
        var po = NewDraftPo(("SKU-A", 100));
        po.Cancel("withdrawn", Now);

        var result = po.RecordLineReceipt(po.Lines.Single().Id, 10, Now.AddMinutes(1));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("po.invalid_state");
    }

    [Fact]
    public void RecordLineReceipt_UnknownLineId_FailsWithCode()
    {
        var po = NewDraftPo(("SKU-A", 100));
        po.Open(Now);

        var result = po.RecordLineReceipt(Guid.NewGuid(), 1, Now.AddMinutes(1));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("po.line_not_found");
    }

    [Fact]
    public void RecordLineReceipt_TwoLinesBothFull_ClosesPoOnSecond()
    {
        var po = NewDraftPo(("SKU-A", 10), ("SKU-B", 20));
        po.Open(Now);
        var a = po.Lines.First(l => l.Sku == "SKU-A");
        var b = po.Lines.First(l => l.Sku == "SKU-B");

        po.RecordLineReceipt(a.Id, 10, Now.AddMinutes(1));
        po.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);

        po.RecordLineReceipt(b.Id, 20, Now.AddMinutes(2));
        po.Status.Should().Be(PurchaseOrderStatus.Closed);
    }
}
