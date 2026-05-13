using ShopFlow.Channel.Domain.Channels;
using ShopFlow.Channel.Domain.ProductMappings;
using ShopFlow.Channel.Domain.Webhooks;
using ChannelAggregate = ShopFlow.Channel.Domain.Channels.Channel;

namespace ShopFlow.Channel.UnitTests;

/// <summary>
/// Sprint-4 plan U1 — Channel Domain coverage. Locks the aggregate factory
/// + state-machine contracts so U2's EF mapping + U3's receiver can rely on
/// them without re-deriving the invariants.
/// </summary>
public sealed class ChannelDomainTests
{
    private static readonly DateTime Now = new(2026, 5, 13, 10, 0, 0, DateTimeKind.Utc);

    // -------------------- Channel aggregate --------------------

    [Fact]
    public void Channel_Create_ReturnsActive_WhenInputsValid()
    {
        var channelId = Guid.NewGuid();
        var result = ChannelAggregate.Create(channelId, "Shopee");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(channelId);
        result.Value!.ChannelType.Should().Be("shopee"); // normalized lowercase
        result.Value!.Status.Should().Be(ChannelStatus.Active);
    }

    [Fact]
    public void Channel_Create_FailsOnEmptyId()
    {
        var result = ChannelAggregate.Create(Guid.Empty, "shopee");
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("channel.channel_id_required");
    }

    [Fact]
    public void Channel_Create_FailsOnBlankType()
    {
        var result = ChannelAggregate.Create(Guid.NewGuid(), "   ");
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("channel.channel_type_required");
    }

    [Fact]
    public void Channel_Create_FailsOnOverlongType()
    {
        var result = ChannelAggregate.Create(Guid.NewGuid(), new string('a', 33));
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("channel.channel_type_too_long");
    }

    [Fact]
    public void Channel_Disable_FlipsStatus()
    {
        var channel = ChannelAggregate.Create(Guid.NewGuid(), "shopee").Value!;
        var result = channel.Disable(Now);

        result.IsSuccess.Should().BeTrue();
        channel.Status.Should().Be(ChannelStatus.Disabled);
        channel.DisabledAt.Should().Be(Now);
    }

    [Fact]
    public void Channel_Disable_IsIdempotent()
    {
        var channel = ChannelAggregate.Create(Guid.NewGuid(), "shopee").Value!;
        channel.Disable(Now);

        var second = channel.Disable(Now.AddMinutes(1));

        second.IsSuccess.Should().BeTrue();
        channel.DisabledAt.Should().Be(Now); // first-write wins, no re-stamp
    }

    // -------------------- ProviderEventId value object --------------------

    [Fact]
    public void ProviderEventId_Create_TrimsAndAccepts()
    {
        var result = ProviderEventId.Create("  evt-abc-123  ");
        result.IsSuccess.Should().BeTrue();
        result.Value!.Value.Should().Be("evt-abc-123");
    }

    [Fact]
    public void ProviderEventId_Create_FailsOnEmpty()
    {
        var result = ProviderEventId.Create(string.Empty);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("webhook.provider_event_id_required");
    }

    [Fact]
    public void ProviderEventId_Create_FailsOnWhitespace()
    {
        var result = ProviderEventId.Create("   ");
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("webhook.provider_event_id_required");
    }

    [Fact]
    public void ProviderEventId_Create_FailsOnOverlong()
    {
        var result = ProviderEventId.Create(new string('x', 201));
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("webhook.provider_event_id_too_long");
    }

    [Fact]
    public void ProviderEventId_Equality_IsCaseSensitive()
    {
        var lower = ProviderEventId.Create("EvtABC").Value!;
        var upper = ProviderEventId.Create("evtabc").Value!;
        lower.Equals(upper).Should().BeFalse();
    }

    // -------------------- WebhookEvent aggregate --------------------

    [Fact]
    public void WebhookEvent_Create_ReturnsReceivedRow_WhenInputsValid()
    {
        var channelId = Guid.NewGuid();
        var providerEventId = ProviderEventId.Create("evt-1").Value!;

        var result = WebhookEvent.Create(channelId, providerEventId, "{\"k\":1}", signatureVerified: true);

        result.IsSuccess.Should().BeTrue();
        var evt = result.Value!;
        evt.ChannelId.Should().Be(channelId);
        evt.ProviderEventId.Should().Be(providerEventId);
        evt.Payload.Should().Be("{\"k\":1}");
        evt.SignatureVerified.Should().BeTrue();
        evt.Status.Should().Be(WebhookProcessingStatus.Received);
        evt.ProcessedAt.Should().BeNull();
    }

    [Fact]
    public void WebhookEvent_Create_FailsOnEmptyChannelId()
    {
        var providerEventId = ProviderEventId.Create("evt-1").Value!;
        var result = WebhookEvent.Create(Guid.Empty, providerEventId, "{}", true);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("webhook.channel_id_required");
    }

    [Fact]
    public void WebhookEvent_Create_FailsOnNullPayload()
    {
        var providerEventId = ProviderEventId.Create("evt-1").Value!;
        var result = WebhookEvent.Create(Guid.NewGuid(), providerEventId, null!, true);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("webhook.payload_required");
    }

    [Fact]
    public void WebhookEvent_MarkProcessed_FromReceived_Succeeds()
    {
        var evt = WebhookEvent.Create(Guid.NewGuid(), ProviderEventId.Create("e").Value!, "{}", true).Value!;
        var result = evt.MarkProcessed(Now);

        result.IsSuccess.Should().BeTrue();
        evt.Status.Should().Be(WebhookProcessingStatus.Processed);
        evt.ProcessedAt.Should().Be(Now);
    }

    [Fact]
    public void WebhookEvent_MarkProcessed_IsIdempotent()
    {
        var evt = WebhookEvent.Create(Guid.NewGuid(), ProviderEventId.Create("e").Value!, "{}", true).Value!;
        evt.MarkProcessed(Now);

        var second = evt.MarkProcessed(Now.AddMinutes(5));
        second.IsSuccess.Should().BeTrue();
        evt.ProcessedAt.Should().Be(Now);
    }

    [Fact]
    public void WebhookEvent_MarkProcessed_FromFailed_Rejected()
    {
        var evt = WebhookEvent.Create(Guid.NewGuid(), ProviderEventId.Create("e").Value!, "{}", true).Value!;
        evt.MarkFailed("unmappable sku", Now);

        var result = evt.MarkProcessed(Now.AddMinutes(1));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("webhook.invalid_state");
    }

    [Fact]
    public void WebhookEvent_MarkFailed_FromReceived_Succeeds()
    {
        var evt = WebhookEvent.Create(Guid.NewGuid(), ProviderEventId.Create("e").Value!, "{}", true).Value!;
        var result = evt.MarkFailed("sku M-001 unmapped", Now);

        result.IsSuccess.Should().BeTrue();
        evt.Status.Should().Be(WebhookProcessingStatus.Failed);
        evt.FailureReason.Should().Be("sku M-001 unmapped");
        evt.ProcessedAt.Should().Be(Now);
    }

    [Fact]
    public void WebhookEvent_MarkFailed_RequiresReason()
    {
        var evt = WebhookEvent.Create(Guid.NewGuid(), ProviderEventId.Create("e").Value!, "{}", true).Value!;
        var result = evt.MarkFailed("  ", Now);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("webhook.failure_reason_required");
    }

    [Fact]
    public void WebhookEvent_MarkFailed_FromProcessed_Rejected()
    {
        var evt = WebhookEvent.Create(Guid.NewGuid(), ProviderEventId.Create("e").Value!, "{}", true).Value!;
        evt.MarkProcessed(Now);

        var result = evt.MarkFailed("late failure", Now.AddMinutes(1));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("webhook.invalid_state");
    }

    // -------------------- ExternalSku value object --------------------

    [Fact]
    public void ExternalSku_Create_TrimsAndAccepts()
    {
        var result = ExternalSku.Create("  sku-001  ");
        result.IsSuccess.Should().BeTrue();
        result.Value!.Value.Should().Be("sku-001");
    }

    [Fact]
    public void ExternalSku_Create_FailsOnEmpty()
    {
        var result = ExternalSku.Create(string.Empty);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("mapping.external_sku_required");
    }

    [Fact]
    public void ExternalSku_Create_FailsOnOverlong()
    {
        var result = ExternalSku.Create(new string('a', 129));
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("mapping.external_sku_too_long");
    }

    [Fact]
    public void ExternalSku_Equality_IsCaseInsensitiveOrdinal()
    {
        var lower = ExternalSku.Create("sku-001").Value!;
        var upper = ExternalSku.Create("SKU-001").Value!;
        var mixed = ExternalSku.Create("Sku-001").Value!;

        lower.Equals(upper).Should().BeTrue();
        lower.Equals(mixed).Should().BeTrue();
        lower.GetHashCode().Should().Be(upper.GetHashCode());
    }

    // -------------------- ProductMapping aggregate --------------------

    [Fact]
    public void ProductMapping_Create_Exact_RequiresConfidenceOne()
    {
        var ok = ProductMapping.Create(Guid.NewGuid(), ExternalSku.Create("ext").Value!, "int", MappingMethod.Exact, 1m);
        ok.IsSuccess.Should().BeTrue();
        ok.Value!.ConfidenceScore.Should().Be(1m);

        var bad = ProductMapping.Create(Guid.NewGuid(), ExternalSku.Create("ext").Value!, "int", MappingMethod.Exact, 0.9m);
        bad.IsSuccess.Should().BeFalse();
        bad.ErrorCode.Should().Be("mapping.exact_confidence_mismatch");
    }

    [Fact]
    public void ProductMapping_Create_Fuzzy_AcceptsAboveThreshold()
    {
        var ok = ProductMapping.Create(Guid.NewGuid(), ExternalSku.Create("ext").Value!, "int", MappingMethod.Fuzzy, 0.75m);
        ok.IsSuccess.Should().BeTrue();
        ok.Value!.ConfidenceScore.Should().Be(0.75m);
        ok.Value!.Method.Should().Be(MappingMethod.Fuzzy);
    }

    [Fact]
    public void ProductMapping_Create_Fuzzy_RejectsBelowThreshold()
    {
        var bad = ProductMapping.Create(Guid.NewGuid(), ExternalSku.Create("ext").Value!, "int", MappingMethod.Fuzzy, 0.49m);
        bad.IsSuccess.Should().BeFalse();
        bad.ErrorCode.Should().Be("mapping.fuzzy_below_threshold");
    }

    [Fact]
    public void ProductMapping_Create_Manual_ForcesConfidenceOne()
    {
        // Manual overrides whatever confidence the caller passed.
        var result = ProductMapping.Create(
            Guid.NewGuid(),
            ExternalSku.Create("ext").Value!,
            "int",
            MappingMethod.Manual,
            0.6m
        );

        result.IsSuccess.Should().BeTrue();
        result.Value!.ConfidenceScore.Should().Be(1m);
        result.Value!.Method.Should().Be(MappingMethod.Manual);
    }

    [Fact]
    public void ProductMapping_Create_FailsOnEmptyChannelId()
    {
        var result = ProductMapping.Create(
            Guid.Empty,
            ExternalSku.Create("ext").Value!,
            "int",
            MappingMethod.Exact,
            1m
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("mapping.channel_id_required");
    }

    [Fact]
    public void ProductMapping_Create_FailsOnBlankInternalSku()
    {
        var result = ProductMapping.Create(
            Guid.NewGuid(),
            ExternalSku.Create("ext").Value!,
            "  ",
            MappingMethod.Exact,
            1m
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("mapping.internal_sku_required");
    }

    [Fact]
    public void ProductMapping_Create_FailsOnConfidenceOutOfRange()
    {
        var bad = ProductMapping.Create(
            Guid.NewGuid(),
            ExternalSku.Create("ext").Value!,
            "int",
            MappingMethod.Fuzzy,
            1.5m
        );

        bad.IsSuccess.Should().BeFalse();
        bad.ErrorCode.Should().Be("mapping.confidence_out_of_range");
    }
}
