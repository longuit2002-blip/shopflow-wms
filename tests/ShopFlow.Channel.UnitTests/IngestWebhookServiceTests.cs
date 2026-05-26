using NSubstitute;
using ShopFlow.Channel.Application.Ports;
using ShopFlow.Channel.Application.Webhooks;
using ShopFlow.Channel.Domain.Webhooks;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Channel.UnitTests;

/// <summary>
/// Sprint-4 plan U3 — IngestWebhookService orchestrator coverage. Locks the
/// load-bearing invariant: replays do NOT write a second outbox row.
/// Repository concurrency (UNIQUE-23505 catch) is verified by integration
/// tests at the EF + Postgres level (deferred to CI per Sprint-1/3
/// precedent — Docker required).
/// </summary>
public sealed class IngestWebhookServiceTests
{
    private static readonly Guid ChannelId = Guid.NewGuid();

    private static WebhookEnvelope NewEnvelope(string providerEventId = "evt-1") =>
        new(
            ChannelId,
            providerEventId,
            EventType: "order.created",
            RawPayload: "{}",
            OccurredAt: new DateTime(2026, 5, 13, 10, 0, 0, DateTimeKind.Utc)
        );

    [Fact]
    public async Task IngestAsync_FirstWrite_AppendsOutbox_AndSaves()
    {
        var repo = Substitute.For<IWebhookEventRepository>();
        var outbox = Substitute.For<IChannelOutbox>();
        var uow = Substitute.For<IUnitOfWork>();

        repo.TryInsertAsync(Arg.Any<WebhookEvent>(), Arg.Any<CancellationToken>())
            .Returns(
                Result<TryInsertWebhookResult>.Success(
                    new TryInsertWebhookResult(Guid.NewGuid(), IsDuplicate: false)
                )
            );

        var sut = new IngestWebhookService(repo, outbox, uow);

        var result = await sut.IngestAsync(
            NewEnvelope(),
            downstreamEventType: "X.Y.Z",
            downstreamPayload: new { ok = true },
            ct: default
        );

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsDuplicate.Should().BeFalse();

        await outbox
            .Received(1)
            .AppendAsync("X.Y.Z", Arg.Any<object>(), Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_Replay_DoesNotAppendOutbox_DoesNotSave()
    {
        var existingId = Guid.NewGuid();
        var repo = Substitute.For<IWebhookEventRepository>();
        var outbox = Substitute.For<IChannelOutbox>();
        var uow = Substitute.For<IUnitOfWork>();

        repo.TryInsertAsync(Arg.Any<WebhookEvent>(), Arg.Any<CancellationToken>())
            .Returns(
                Result<TryInsertWebhookResult>.Success(
                    new TryInsertWebhookResult(existingId, IsDuplicate: true)
                )
            );

        var sut = new IngestWebhookService(repo, outbox, uow);

        var result = await sut.IngestAsync(
            NewEnvelope(),
            downstreamEventType: "X.Y.Z",
            downstreamPayload: new { ok = true },
            ct: default
        );

        result.IsSuccess.Should().BeTrue();
        result.Value!.EventId.Should().Be(existingId);
        result.Value!.IsDuplicate.Should().BeTrue();

        await outbox
            .DidNotReceive()
            .AppendAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_BlankProviderEventId_FailsAtDomain()
    {
        var repo = Substitute.For<IWebhookEventRepository>();
        var outbox = Substitute.For<IChannelOutbox>();
        var uow = Substitute.For<IUnitOfWork>();

        var sut = new IngestWebhookService(repo, outbox, uow);

        var result = await sut.IngestAsync(
            NewEnvelope(providerEventId: "   "),
            downstreamEventType: "X.Y.Z",
            downstreamPayload: new { },
            ct: default
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("webhook.provider_event_id_required");

        await repo.DidNotReceive()
            .TryInsertAsync(Arg.Any<WebhookEvent>(), Arg.Any<CancellationToken>());
        await outbox
            .DidNotReceive()
            .AppendAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_RepositoryFailure_PropagatesAndSkipsOutbox()
    {
        var repo = Substitute.For<IWebhookEventRepository>();
        var outbox = Substitute.For<IChannelOutbox>();
        var uow = Substitute.For<IUnitOfWork>();

        repo.TryInsertAsync(Arg.Any<WebhookEvent>(), Arg.Any<CancellationToken>())
            .Returns(Result<TryInsertWebhookResult>.Failure("db failure", "db.fail"));

        var sut = new IngestWebhookService(repo, outbox, uow);

        var result = await sut.IngestAsync(
            NewEnvelope(),
            downstreamEventType: "X.Y.Z",
            downstreamPayload: new { },
            ct: default
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("db.fail");

        await outbox
            .DidNotReceive()
            .AppendAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_RejectsNullEnvelope()
    {
        var sut = new IngestWebhookService(
            Substitute.For<IWebhookEventRepository>(),
            Substitute.For<IChannelOutbox>(),
            Substitute.For<IUnitOfWork>()
        );

        var act = async () => await sut.IngestAsync(null!, "X.Y.Z", new { }, default);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task IngestAsync_RejectsBlankDownstreamEventType()
    {
        var sut = new IngestWebhookService(
            Substitute.For<IWebhookEventRepository>(),
            Substitute.For<IChannelOutbox>(),
            Substitute.For<IUnitOfWork>()
        );

        var act = async () => await sut.IngestAsync(NewEnvelope(), "   ", new { }, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
