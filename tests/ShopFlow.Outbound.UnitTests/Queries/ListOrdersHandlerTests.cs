using NSubstitute;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Application.Queries;

namespace ShopFlow.Outbound.UnitTests.Queries;

/// <summary>
/// Sprint-7 plan U3 — <see cref="ListOrdersHandler"/> coverage.
/// Mocks <see cref="IOrderRepository"/> so the join-heavy EF query stays in
/// the integration-test surface (Sprint-7 U4 spins up Testcontainers
/// Postgres); the unit tests pin the handler's slicing, filter forwarding,
/// channel-display parsing, and pagination clamp.
/// </summary>
public sealed class ListOrdersHandlerTests
{
    private static readonly DateTime Now = new(2026, 5, 19, 10, 0, 0, DateTimeKind.Utc);

    private static OrderListRow BuildRow(
        string externalId,
        string? currentSagaState = "Reserved",
        DateTime? lastTransitionAt = null,
        int lineCount = 1
    )
    {
        return new OrderListRow(
            Id: Guid.NewGuid(),
            ChannelExternalOrderId: externalId,
            Channel: string.Empty, // repo leaves blank; handler parses
            LineCount: lineCount,
            CurrentSagaState: currentSagaState,
            CreatedAt: Now,
            LastTransitionAt: lastTransitionAt
        );
    }

    private static (ListOrdersHandler handler, IOrderRepository repo) BuildSut()
    {
        var repo = Substitute.For<IOrderRepository>();
        return (new ListOrdersHandler(repo), repo);
    }

    [Fact]
    public async Task Handle_FiltersStatus_ForwardsToRepository()
    {
        var (handler, repo) = BuildSut();
        repo.ListAsync(
                Arg.Any<OrderListFilter>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new OrderListPageResult(new[] { BuildRow("SHOPEE_42"), BuildRow("SHOPEE_43") }, 2)
            );

        var result = await handler.Handle(
            new ListOrdersQuery(new OrderListFilter(Status: "Reserved"), Skip: 0, Take: 50),
            CancellationToken.None
        );

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
        await repo.Received(1)
            .ListAsync(
                Arg.Is<OrderListFilter>(f => f.Status == "Reserved"),
                0,
                50,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Handle_PaginationClamp_AppliesSkipAndTake()
    {
        var (handler, repo) = BuildSut();
        repo.ListAsync(
                Arg.Any<OrderListFilter>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new OrderListPageResult(Array.Empty<OrderListRow>(), 0));

        // Skip < 0 → clamp to 0; Take > MaxTake → clamp to MaxTake (200).
        await handler.Handle(
            new ListOrdersQuery(new OrderListFilter(), Skip: -5, Take: 500),
            CancellationToken.None
        );

        await repo.Received(1)
            .ListAsync(
                Arg.Any<OrderListFilter>(),
                0,
                ListOrdersHandler.MaxTake,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Handle_TakeBelowMinimum_ClampsToOne()
    {
        var (handler, repo) = BuildSut();
        repo.ListAsync(
                Arg.Any<OrderListFilter>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new OrderListPageResult(Array.Empty<OrderListRow>(), 0));

        await handler.Handle(
            new ListOrdersQuery(new OrderListFilter(), Skip: 0, Take: 0),
            CancellationToken.None
        );

        await repo.Received(1)
            .ListAsync(Arg.Any<OrderListFilter>(), 0, 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SearchFilter_ForwardsSubstringSearch()
    {
        var (handler, repo) = BuildSut();
        repo.ListAsync(
                Arg.Any<OrderListFilter>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new OrderListPageResult(new[] { BuildRow("SHOPEE_match-42") }, 1));

        var result = await handler.Handle(
            new ListOrdersQuery(new OrderListFilter(Search: "match"), Skip: 0, Take: 50),
            CancellationToken.None
        );

        result
            .Items.Should()
            .ContainSingle()
            .Which.ChannelExternalOrderId.Should()
            .Be("SHOPEE_match-42");
        await repo.Received(1)
            .ListAsync(
                Arg.Is<OrderListFilter>(f => f.Search == "match"),
                0,
                50,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Handle_LastTransitionAt_FlowsThroughFromRepository()
    {
        var (handler, repo) = BuildSut();
        var transitionTime = Now.AddMinutes(5);
        repo.ListAsync(
                Arg.Any<OrderListFilter>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new OrderListPageResult(
                    new[]
                    {
                        BuildRow("SHOPEE_1", lastTransitionAt: transitionTime),
                        BuildRow("SHOPEE_2", lastTransitionAt: null),
                    },
                    2
                )
            );

        var result = await handler.Handle(
            new ListOrdersQuery(new OrderListFilter(), Skip: 0, Take: 50),
            CancellationToken.None
        );

        result.Items[0].LastTransitionAt.Should().Be(transitionTime);
        result.Items[1].LastTransitionAt.Should().BeNull();
    }

    [Theory]
    [InlineData("SHOPEE_42", "Shopee")]
    [InlineData("LAZADA_77", "Lazada")]
    [InlineData("TIKTOK_99", "TikTok Shop")]
    [InlineData("custom-no-prefix", "Direct")]
    [InlineData("", "Direct")]
    public async Task Handle_ChannelDisplay_ParsedFromPrefix(string externalId, string expected)
    {
        var (handler, repo) = BuildSut();
        repo.ListAsync(
                Arg.Any<OrderListFilter>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new OrderListPageResult(new[] { BuildRow(externalId) }, 1));

        var result = await handler.Handle(
            new ListOrdersQuery(new OrderListFilter(), Skip: 0, Take: 50),
            CancellationToken.None
        );

        result.Items.Should().ContainSingle().Which.Channel.Should().Be(expected);
    }

    [Fact]
    public async Task Handle_EmptyResult_ReturnsEmptyPage()
    {
        var (handler, repo) = BuildSut();
        repo.ListAsync(
                Arg.Any<OrderListFilter>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new OrderListPageResult(Array.Empty<OrderListRow>(), 0));

        var result = await handler.Handle(
            new ListOrdersQuery(new OrderListFilter(Status: "DoesNotMatch"), Skip: 0, Take: 50),
            CancellationToken.None
        );

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NullRequest_Throws()
    {
        var (handler, _) = BuildSut();

        Func<Task> act = () => handler.Handle(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
