using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.UnitTests.Domain;

/// <summary>
/// Sprint-3-redux U2 — <see cref="Order"/> aggregate state machine +
/// validation. Mirrors Sprint-2-redux's <c>PurchaseOrderTests</c> shape:
/// every domain transition exercised in isolation against the public
/// <c>Result</c> contract.
/// </summary>
public sealed class OrderTests
{
    private static IReadOnlyList<(string Sku, int Qty, int? ExpectedWeight)> TwoLines() =>
        new[] { ("SKU-A", 2, (int?)100), ("SKU-B", 5, (int?)50) };

    private static Order NewCreatedOrder() => Order.Create("ext-1", "standard", TwoLines()).Value!;

    // ── Create -------------------------------------------------------------

    [Fact]
    public void Create_HappyPath_ProducesCreatedOrderWithLinesAndWeight()
    {
        var result = Order.Create("ext-1", "standard", TwoLines());

        result.IsSuccess.Should().BeTrue();
        var order = result.Value!;
        order.Status.Should().Be(OrderStatus.Created);
        order.ChannelExternalOrderId.Should().Be("ext-1");
        order.ShippingProfile.Should().Be("standard");
        order.Lines.Should().HaveCount(2);
        // (2 * 100) + (5 * 50) = 450
        order.ExpectedWeightTotal.Should().Be(450);
    }

    [Fact]
    public void Create_EmptyLines_FailsWithCode()
    {
        var result = Order.Create("ext-1", "standard", Array.Empty<(string, int, int?)>());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order.no_lines");
    }

    [Fact]
    public void Create_NonPositiveQty_FailsWithCode()
    {
        var result = Order.Create("ext-1", "standard", new[] { ("SKU-A", 0, (int?)null) });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order_line.qty_non_positive");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankExternalId_FailsWithCode(string externalId)
    {
        var result = Order.Create(externalId, "standard", new[] { ("SKU-A", 1, (int?)null) });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order.external_id_required");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankShippingProfile_FailsWithCode(string shippingProfile)
    {
        var result = Order.Create("ext-1", shippingProfile, new[] { ("SKU-A", 1, (int?)null) });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order.shipping_profile_required");
    }

    [Fact]
    public void Create_BlankSku_FailsWithCode()
    {
        var result = Order.Create("ext-1", "standard", new[] { ("", 1, (int?)null) });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order_line.sku_required");
    }

    [Fact]
    public void Create_AnyLineLacksWeight_ExpectedWeightTotalIsNull()
    {
        var result = Order.Create(
            "ext-1",
            "standard",
            new[] { ("SKU-A", 1, (int?)100), ("SKU-B", 1, (int?)null) }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value!.ExpectedWeightTotal.Should().BeNull();
    }

    [Fact]
    public void Create_NegativeExpectedWeight_FailsWithCode()
    {
        var result = Order.Create("ext-1", "standard", new[] { ("SKU-A", 1, (int?)-5) });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order_line.expected_weight_negative");
    }

    [Fact]
    public void Create_TrimsExternalIdAndShippingProfile()
    {
        var order = Order
            .Create(" ext-1 ", " standard ", new[] { (" SKU-A ", 1, (int?)null) })
            .Value!;

        order.ChannelExternalOrderId.Should().Be("ext-1");
        order.ShippingProfile.Should().Be("standard");
        order.Lines.Single().Sku.Should().Be("SKU-A");
    }

    // ── State transitions: happy chain ------------------------------------

    [Fact]
    public void HappyChain_DrivesOrderFromCreatedToShipped()
    {
        var order = NewCreatedOrder();

        order.MarkAwaitingReservation().IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.AwaitingReservation);

        order.MarkReserved().IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Reserved);

        order.MarkAwaitingPick().IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.AwaitingPick);

        order.MarkPicked().IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Picked);

        order.MarkAwaitingPack().IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.AwaitingPack);

        // MarkPacked needs Picked pre-state (not AwaitingPack).
        // The saga model in the plan uses Picked → Packed directly; see plan.
        // Reset to Picked for this transition by re-creating the order.
        var order2 = NewCreatedOrder();
        order2.MarkAwaitingReservation();
        order2.MarkReserved();
        order2.MarkAwaitingPick();
        order2.MarkPicked();
        order2.MarkPacked(actualWeightTotal: 450).IsSuccess.Should().BeTrue();
        order2.Status.Should().Be(OrderStatus.Packed);
        order2.ActualWeightTotal.Should().Be(450);

        order2.MarkAwaitingShip().IsSuccess.Should().BeTrue();
        order2.Status.Should().Be(OrderStatus.AwaitingShip);

        order2.MarkShipped("https://carrier/label/abc", "TRK-001").IsSuccess.Should().BeTrue();
        order2.Status.Should().Be(OrderStatus.Shipped);
        order2.LabelUrl.Should().Be("https://carrier/label/abc");
        order2.TrackingNumber.Should().Be("TRK-001");
    }

    // ── State transitions: defensive failures -----------------------------

    [Fact]
    public void MarkReserved_FromCreated_FailsInvalidState()
    {
        var order = NewCreatedOrder();

        var result = order.MarkReserved();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order.invalid_state");
    }

    [Fact]
    public void MarkAwaitingPick_FromAwaitingReservation_FailsInvalidState()
    {
        var order = NewCreatedOrder();
        order.MarkAwaitingReservation();

        var result = order.MarkAwaitingPick();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order.invalid_state");
    }

    [Fact]
    public void MarkPicked_FromCreated_FailsInvalidState()
    {
        var order = NewCreatedOrder();

        var result = order.MarkPicked();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order.invalid_state");
    }

    [Fact]
    public void MarkPacked_FromAwaitingPack_FailsInvalidState()
    {
        // MarkPacked requires Picked pre-state, not AwaitingPack — the
        // saga publishes Pack confirmation that includes the actual
        // weight, and the order goes Picked → Packed (then →
        // AwaitingShip → Shipped). AwaitingPack is a saga-internal
        // bookkeeping state without a transition on the aggregate.
        var order = NewCreatedOrder();
        order.MarkAwaitingReservation();
        order.MarkReserved();
        order.MarkAwaitingPick();
        order.MarkPicked();
        order.MarkAwaitingPack();

        var result = order.MarkPacked(actualWeightTotal: 100);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order.invalid_state");
    }

    [Fact]
    public void MarkShipped_FromPacked_FailsInvalidState()
    {
        var order = NewCreatedOrder();
        order.MarkAwaitingReservation();
        order.MarkReserved();
        order.MarkAwaitingPick();
        order.MarkPicked();
        order.MarkPacked(100);

        var result = order.MarkShipped("url", "TRK");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order.invalid_state");
    }

    [Theory]
    [InlineData("", "TRK")]
    [InlineData("   ", "TRK")]
    public void MarkShipped_BlankLabelUrl_FailsWithCode(string labelUrl, string trackingNumber)
    {
        var order = ReadyForShip();

        var result = order.MarkShipped(labelUrl, trackingNumber);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order.label_url_required");
    }

    [Theory]
    [InlineData("url", "")]
    [InlineData("url", "   ")]
    public void MarkShipped_BlankTracking_FailsWithCode(string labelUrl, string trackingNumber)
    {
        var order = ReadyForShip();

        var result = order.MarkShipped(labelUrl, trackingNumber);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order.tracking_number_required");
    }

    // ── Compensation path -------------------------------------------------

    [Fact]
    public void MarkCompensatingReservation_FromReserved_TransitionsOk()
    {
        var order = NewCreatedOrder();
        order.MarkAwaitingReservation();
        order.MarkReserved();

        var result = order.MarkCompensatingReservation();

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.CompensatingReservation);
    }

    [Fact]
    public void MarkCompensatingReservation_FromCreated_FailsInvalidState()
    {
        var order = NewCreatedOrder();

        var result = order.MarkCompensatingReservation();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order.invalid_state");
    }

    // Sprint-12.5 U3 — Path C: MarkCompensatingReservation widens to allow
    // AwaitingShip pre-state (Order aggregate's state when mark-ship-failed
    // fires; saga state is still Packed at that moment per Sprint-12 KTD2).
    [Fact]
    public void MarkCompensatingReservation_FromAwaitingShip_TransitionsOk()
    {
        var order = NewCreatedOrder();
        order.MarkAwaitingReservation();
        order.MarkReserved();
        order.MarkAwaitingPick();
        order.MarkPicked();
        order.MarkPacked(100);
        order.MarkAwaitingShip();

        var result = order.MarkCompensatingReservation();

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.CompensatingReservation);
    }

    // Sprint-13 U3 — Path D: MarkCompensatingReservation widens to allow
    // Picked pre-state (Order aggregate's state when mark-pack-failed fires;
    // saga state is also Picked at that moment per Sprint-13 K1 — the Order
    // never rests in AwaitingPack because ConfirmPackAsync chains
    // MarkPacked → MarkAwaitingShip atomically).
    [Fact]
    public void MarkCompensatingReservation_FromPicked_TransitionsOk()
    {
        var order = NewCreatedOrder();
        order.MarkAwaitingReservation();
        order.MarkReserved();
        order.MarkAwaitingPick();
        order.MarkPicked();

        var result = order.MarkCompensatingReservation();

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.CompensatingReservation);
    }

    [Fact]
    public void MarkCompensatingReservation_FromPacked_FailsInvalidState()
    {
        // Packed is a transient state — ConfirmPackAsync auto-chains to
        // AwaitingShip in one commit. If a test ever lands a row in Packed
        // and tries to compensate, the domain correctly rejects. (Sprint-13
        // K1 — this stays unchanged: Picked widens the allow-set, Packed
        // does NOT, because Packed is never at rest.)
        var order = NewCreatedOrder();
        order.MarkAwaitingReservation();
        order.MarkReserved();
        order.MarkAwaitingPick();
        order.MarkPicked();
        order.MarkPacked(100);

        var result = order.MarkCompensatingReservation();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order.invalid_state");
    }

    [Fact]
    public void MarkCompensatingReservation_FromShipped_FailsInvalidState()
    {
        var order = NewCreatedOrder();
        order.MarkAwaitingReservation();
        order.MarkReserved();
        order.MarkAwaitingPick();
        order.MarkPicked();
        order.MarkPacked(100);
        order.MarkAwaitingShip();
        order.MarkShipped("https://example/label.pdf", "TRACK-001");

        var result = order.MarkCompensatingReservation();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order.invalid_state");
    }

    [Fact]
    public void MarkCancelled_FromCreated_Succeeds()
    {
        var order = NewCreatedOrder();

        var result = order.MarkCancelled();

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void MarkCancelled_FromCompensatingReservation_Succeeds()
    {
        var order = NewCreatedOrder();
        order.MarkAwaitingReservation();
        order.MarkReserved();
        order.MarkCompensatingReservation();

        var result = order.MarkCancelled();

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void MarkCancelled_FromShipped_FailsInvalidState()
    {
        var order = ReadyForShip();
        order.MarkShipped("url", "TRK");

        var result = order.MarkCancelled();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order.invalid_state");
    }

    [Fact]
    public void MarkCancelled_AlreadyCancelled_FailsAlreadyCancelled()
    {
        var order = NewCreatedOrder();
        order.MarkCancelled();

        var result = order.MarkCancelled();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order.already_cancelled");
    }

    // ── Pick wave attachment ---------------------------------------------

    [Fact]
    public void AttachToPickWave_HappyPath_RecordsWaveId()
    {
        var order = NewCreatedOrder();
        var waveId = Guid.NewGuid();

        var result = order.AttachToPickWave(waveId);

        result.IsSuccess.Should().BeTrue();
        order.PickWaveId.Should().Be(waveId);
    }

    [Fact]
    public void AttachToPickWave_EmptyGuid_FailsWithCode()
    {
        var order = NewCreatedOrder();

        var result = order.AttachToPickWave(Guid.Empty);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("order.pick_wave_id_required");
    }

    private static Order ReadyForShip()
    {
        var order = NewCreatedOrder();
        order.MarkAwaitingReservation();
        order.MarkReserved();
        order.MarkAwaitingPick();
        order.MarkPicked();
        order.MarkPacked(100);
        order.MarkAwaitingShip();
        return order;
    }
}
