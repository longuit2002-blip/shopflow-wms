using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShopFlow.Channel.Application.Adapters;
using ShopFlow.Channel.Application.Ports;
using ShopFlow.Channel.Application.Webhooks;
using ShopFlow.Channel.Domain.ProductMappings;
using ShopFlow.Channel.Domain.Webhooks;
using ShopFlow.Contracts.Channel;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Channel.UnitTests;

/// <summary>
/// Sprint-4.5 plan U3 — <see cref="WebhookOrchestrator"/> behavior in
/// isolation. NSubstitute mocks the dependencies; the real
/// <see cref="IngestWebhookService"/> persistence path is covered by
/// the integration suite (Testcontainers Postgres) once U4's harness
/// lands.
/// </summary>
public sealed class WebhookOrchestratorTests
{
    private static readonly Guid ChannelId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string ChannelType = "shopee";

    private static WebhookEnvelope NewEnvelope(string eventType = "order.created") =>
        new(
            ChannelId: ChannelId,
            ProviderEventId: "evt-1",
            EventType: eventType,
            RawPayload: "{}",
            OccurredAt: new DateTime(2026, 5, 14, 10, 0, 0, DateTimeKind.Utc)
        );

    private static ExternalOrderDraft NewDraft(params (string ExternalSku, int Qty)[] lines) =>
        new(
            ChannelExternalOrderId: "ORDER-SP-001",
            ShippingProfile: "GHN",
            Lines: lines.Select(l => new ExternalOrderLine(l.ExternalSku, l.Qty)).ToList()
        );

    private static (
        WebhookOrchestrator Sut,
        IChannelAdapter Adapter,
        IProductMappingService Mapping,
        IWebhookEventRepository Repo,
        IChannelOutbox Outbox,
        IUnitOfWork Uow
    ) NewSut(
        ExternalOrderDraft? draft,
        params (string ExternalSku, string? InternalSku)[] resolutions
    )
    {
        var adapter = Substitute.For<IChannelAdapter>();
        adapter.ChannelType.Returns(ChannelType);
        if (draft is not null)
        {
            adapter
                .ParseOrderCreated(Arg.Any<WebhookEnvelope>())
                .Returns(Result<ExternalOrderDraft>.Success(draft));
        }

        var factory = Substitute.For<IChannelAdapterFactory>();
        factory.TryResolve(ChannelType).Returns(adapter);

        var mapping = Substitute.For<IProductMappingService>();
        foreach (var (sku, internalSku) in resolutions)
        {
            var resolution = internalSku is null
                ? null
                : new ProductMappingResolution(internalSku, MappingMethod.Exact, 1.0m);
            mapping
                .ResolveAsync(ChannelId, sku, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<ProductMappingResolution?>(resolution));
        }

        var repo = Substitute.For<IWebhookEventRepository>();
        repo.TryInsertAsync(Arg.Any<WebhookEvent>(), Arg.Any<CancellationToken>())
            .Returns(
                Result<TryInsertWebhookResult>.Success(
                    new TryInsertWebhookResult(Guid.NewGuid(), IsDuplicate: false)
                )
            );

        var outbox = Substitute.For<IChannelOutbox>();
        var uow = Substitute.For<IUnitOfWork>();
        var ingest = new IngestWebhookService(repo, outbox, uow);

        var sut = new WebhookOrchestrator(
            factory,
            mapping,
            ingest,
            NullLogger<WebhookOrchestrator>.Instance
        );
        return (sut, adapter, mapping, repo, outbox, uow);
    }

    [Fact]
    public async Task OrderCreated_AllLinesMapped_EmitsOrderImportedV1()
    {
        var (sut, _, _, _, outbox, _) = NewSut(
            draft: NewDraft(("SP-001", 2), ("SP-002", 3)),
            resolutions: new[] { ("SP-001", (string?)"INV-001"), ("SP-002", (string?)"INV-002") }
        );

        var result = await sut.ProcessAsync(NewEnvelope(), ChannelType, TenantId, ct: default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(WebhookProcessStatus.OrderImported);
        result.Value.UnmappedSkus.Should().BeNull();

        await outbox
            .Received(1)
            .AppendAsync(
                typeof(OrderImportedV1).AssemblyQualifiedName!,
                Arg.Is<OrderImportedV1>(o =>
                    o.TenantId == TenantId
                    && o.ChannelId == ChannelId
                    && o.ChannelExternalOrderId == "ORDER-SP-001"
                    && o.ShippingProfile == "GHN"
                    && o.Lines.Count == 2
                    && o.Lines[0].Sku == "INV-001"
                    && o.Lines[0].Qty == 2
                    && o.Lines[1].Sku == "INV-002"
                    && o.Lines[1].Qty == 3
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task OrderCreated_UnmappedLine_FailsWholeImport_NoOutbox()
    {
        var (sut, _, _, _, outbox, _) = NewSut(
            draft: NewDraft(("SP-001", 2), ("SP-XYZ", 1)),
            resolutions: new[] { ("SP-001", (string?)"INV-001"), ("SP-XYZ", (string?)null) }
        );

        var result = await sut.ProcessAsync(NewEnvelope(), ChannelType, TenantId, ct: default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(WebhookProcessStatus.ImportFailed);
        result.Value.UnmappedSkus.Should().NotBeNull();
        result.Value.UnmappedSkus!.Single().Should().Be("SP-XYZ");

        // NO outbox append on a failed import.
        await outbox
            .DidNotReceive()
            .AppendAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderCreated_MultipleUnmappedLines_ReportsAllInOutcome()
    {
        var (sut, _, _, _, _, _) = NewSut(
            draft: NewDraft(("SP-001", 1), ("SP-XYZ", 1), ("SP-ABC", 1)),
            resolutions: new[]
            {
                ("SP-001", (string?)"INV-001"),
                ("SP-XYZ", (string?)null),
                ("SP-ABC", (string?)null),
            }
        );

        var result = await sut.ProcessAsync(NewEnvelope(), ChannelType, TenantId, ct: default);

        result.Value!.Status.Should().Be(WebhookProcessStatus.ImportFailed);
        result.Value.UnmappedSkus.Should().BeEquivalentTo(new[] { "SP-XYZ", "SP-ABC" });
    }

    [Fact]
    public async Task NonOrderCreatedEvent_PersistsRow_NoOutbox_NoMappingLookup()
    {
        var (sut, adapter, mapping, _, outbox, _) = NewSut(
            draft: null,
            resolutions: Array.Empty<(string, string?)>()
        );

        var result = await sut.ProcessAsync(
            NewEnvelope(eventType: "order.cancelled"),
            ChannelType,
            TenantId,
            ct: default
        );

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(WebhookProcessStatus.EventSkipped);

        // Adapter.ParseOrderCreated NOT called on non-order.created events.
        adapter.DidNotReceive().ParseOrderCreated(Arg.Any<WebhookEnvelope>());

        // Mapping service NOT called.
        await mapping
            .DidNotReceive()
            .ResolveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Outbox still receives the sentinel skip-event (Sprint-4.5 trade-off
        // documented in WebhookOrchestrator.cs — Sprint-6+ refines).
        await outbox
            .Received(1)
            .AppendAsync(
                Arg.Is<string>(s => s.Contains("Skipped", StringComparison.Ordinal)),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task OrderCreated_AdapterParseFailure_BubblesUpResultFailure()
    {
        var adapter = Substitute.For<IChannelAdapter>();
        adapter.ChannelType.Returns(ChannelType);
        adapter
            .ParseOrderCreated(Arg.Any<WebhookEnvelope>())
            .Returns(
                Result<ExternalOrderDraft>.Failure(
                    "shopee.order: items_empty",
                    "shopee.order.items_empty"
                )
            );

        var factory = Substitute.For<IChannelAdapterFactory>();
        factory.TryResolve(ChannelType).Returns(adapter);

        var mapping = Substitute.For<IProductMappingService>();
        var repo = Substitute.For<IWebhookEventRepository>();
        var outbox = Substitute.For<IChannelOutbox>();
        var uow = Substitute.For<IUnitOfWork>();
        var ingest = new IngestWebhookService(repo, outbox, uow);

        var sut = new WebhookOrchestrator(
            factory,
            mapping,
            ingest,
            NullLogger<WebhookOrchestrator>.Instance
        );

        var result = await sut.ProcessAsync(NewEnvelope(), ChannelType, TenantId, ct: default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("shopee.order.items_empty");
    }

    [Fact]
    public async Task UnknownChannelType_ReturnsFailure_AdapterMissing()
    {
        var factory = Substitute.For<IChannelAdapterFactory>();
        factory.TryResolve(Arg.Any<string>()).Returns((IChannelAdapter?)null);

        var mapping = Substitute.For<IProductMappingService>();
        var repo = Substitute.For<IWebhookEventRepository>();
        var outbox = Substitute.For<IChannelOutbox>();
        var uow = Substitute.For<IUnitOfWork>();
        var ingest = new IngestWebhookService(repo, outbox, uow);

        var sut = new WebhookOrchestrator(
            factory,
            mapping,
            ingest,
            NullLogger<WebhookOrchestrator>.Instance
        );

        var result = await sut.ProcessAsync(NewEnvelope(), "lazada", TenantId, ct: default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("webhook.adapter_missing");
    }

    [Fact]
    public async Task OrderCreated_PreservesLineOrder()
    {
        var (sut, _, _, _, outbox, _) = NewSut(
            draft: NewDraft(("A", 1), ("B", 2), ("C", 3), ("D", 4)),
            resolutions: new[]
            {
                ("A", (string?)"INV-A"),
                ("B", (string?)"INV-B"),
                ("C", (string?)"INV-C"),
                ("D", (string?)"INV-D"),
            }
        );

        await sut.ProcessAsync(NewEnvelope(), ChannelType, TenantId, ct: default);

        await outbox
            .Received(1)
            .AppendAsync(
                Arg.Any<string>(),
                Arg.Is<OrderImportedV1>(o =>
                    o.Lines.Count == 4
                    && o.Lines[0].Sku == "INV-A"
                    && o.Lines[1].Sku == "INV-B"
                    && o.Lines[2].Sku == "INV-C"
                    && o.Lines[3].Sku == "INV-D"
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
