using System.Threading.Channels;
using ShopFlow.Outbound.Application;
using ShopFlow.Outbound.Application.Ports;
using PickQueueImpl = ShopFlow.Outbound.Infrastructure.PickQueue.PickQueue;

namespace ShopFlow.Outbound.UnitTests.PickQueueTests;

/// <summary>
/// Sprint-3-redux U5 K3 — <see cref="IPickQueue"/> implementation
/// covering per-tenant channel registry behavior, lazy creation, and
/// 1000-item bounded back-pressure.
/// </summary>
public sealed class PickQueueTests
{
    private static PickRequestV1 Sample(Guid? tenantId = null) =>
        new(
            OrderId: Guid.NewGuid(),
            TenantId: tenantId ?? Guid.NewGuid(),
            ShippingProfile: "standard",
            EnqueuedAt: DateTime.UtcNow,
            LineCount: 2
        );

    [Fact]
    public void GetWriter_FirstCall_ReturnsWriter()
    {
        var queue = new PickQueueImpl();
        var tenantId = Guid.NewGuid();

        var writer = queue.GetWriter(tenantId);

        writer.Should().NotBeNull();
    }

    [Fact]
    public void GetReader_AfterGetWriter_ReturnsSameChannel()
    {
        var queue = new PickQueueImpl();
        var tenantId = Guid.NewGuid();
        var item = Sample(tenantId);
        queue.GetWriter(tenantId).TryWrite(item).Should().BeTrue();

        var reader = queue.GetReader(tenantId);
        reader.TryRead(out var read).Should().BeTrue();

        read.Should().Be(item);
    }

    [Fact]
    public void PerTenantIsolation_TwoTenants_HaveSeparateChannels()
    {
        var queue = new PickQueueImpl();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var itemA = Sample(tenantA);
        var itemB = Sample(tenantB);

        queue.GetWriter(tenantA).TryWrite(itemA).Should().BeTrue();
        queue.GetWriter(tenantB).TryWrite(itemB).Should().BeTrue();

        var readerA = queue.GetReader(tenantA);
        readerA.TryRead(out var readA).Should().BeTrue();
        readA.Should().Be(itemA);
        // Reader A is now empty; reader B still has itemB.
        readerA.TryRead(out _).Should().BeFalse();

        var readerB = queue.GetReader(tenantB);
        readerB.TryRead(out var readB).Should().BeTrue();
        readB.Should().Be(itemB);
    }

    [Fact]
    public async Task BoundedCapacity_FillTo1000Plus1_ForcesBackpressure()
    {
        // The bounded channel capacity is 1000 with FullMode.Wait — the
        // 1001st WriteAsync must NOT complete until something is read.
        var queue = new PickQueueImpl();
        var tenantId = Guid.NewGuid();
        var writer = queue.GetWriter(tenantId);

        for (var i = 0; i < 1000; i++)
        {
            writer.TryWrite(Sample(tenantId)).Should().BeTrue($"item {i} should fit before the cap");
        }

        // 1001st TryWrite — should fail (channel full).
        writer.TryWrite(Sample(tenantId)).Should().BeFalse("the 1001st item exceeds capacity");

        // WriteAsync should block until drain.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var write = writer.WriteAsync(Sample(tenantId), cts.Token);
        // The task should still be pending (back-pressured) — TaskCanceledException after 200ms.
        var act = async () => await write;
        await act.Should().ThrowAsync<OperationCanceledException>(
            "WriteAsync must wait when the channel is full"
        );
    }

    [Fact]
    public void GetWriter_SameTenant_ReturnsSameWriterReference()
    {
        // GetOrAdd's factory runs exactly once per tenant — subsequent
        // calls return the same channel instance.
        var queue = new PickQueueImpl();
        var tenantId = Guid.NewGuid();

        var w1 = queue.GetWriter(tenantId);
        var w2 = queue.GetWriter(tenantId);

        w1.Should().BeSameAs(w2);
    }

    [Fact]
    public void GetReader_GetWriter_OnSameTenant_ShareTheSameChannel()
    {
        var queue = new PickQueueImpl();
        var tenantId = Guid.NewGuid();

        var w = queue.GetWriter(tenantId);
        var r = queue.GetReader(tenantId);

        // Writer + Reader from the same Channel<T> — write must be readable.
        var item = Sample(tenantId);
        w.TryWrite(item).Should().BeTrue();
        r.TryRead(out var read).Should().BeTrue();
        read.Should().Be(item);
    }
}
