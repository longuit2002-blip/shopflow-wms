using FluentAssertions;
using NSubstitute;
using ShopFlow.Inventory.Application.Commands;
using ShopFlow.Inventory.Application.Handlers;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.UnitTests.Application;

public sealed class ReserveStockHandlerTests
{
    private static (
        ReserveStockHandler handler,
        IReservationRepository repo,
        IRequestContext ctx
    ) MakeHandler(Guid? tenantId = null)
    {
        var repo = Substitute.For<IReservationRepository>();
        var ctx = Substitute.For<IRequestContext>();
        ctx.TenantId.Returns(tenantId ?? Guid.NewGuid());
        return (new ReserveStockHandler(repo, ctx), repo, ctx);
    }

    [Fact]
    public async Task Handle_WhenExistingReservationFound_ShortCircuitsWithoutCallingTryReserve()
    {
        var (handler, repo, ctx) = MakeHandler();
        var orderId = Guid.NewGuid();
        var existingId = Guid.NewGuid();
        var existing = new Reservation(
            Id: existingId,
            TenantId: ctx.TenantId,
            Sku: "SKU-001",
            Qty: 3,
            OrderId: orderId,
            Status: ReservationStatus.Active,
            ReservedAt: DateTime.UtcNow,
            ExpiresAt: DateTime.UtcNow.AddMinutes(15),
            FinalizedAt: null
        );
        repo.FindByOrderIdAsync(ctx.TenantId, orderId, Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await handler.Handle(
            new ReserveStockCommand(orderId, "SKU-001", 3),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(existingId);
        await repo.DidNotReceive()
            .TryReserveAsync(
                Arg.Any<Guid>(),
                Arg.Any<Sku>(),
                Arg.Any<int>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Handle_WhenNoExistingReservation_DelegatesToTryReserve()
    {
        var (handler, repo, ctx) = MakeHandler();
        var orderId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        repo.FindByOrderIdAsync(ctx.TenantId, orderId, Arg.Any<CancellationToken>())
            .Returns((Reservation?)null);
        repo.TryReserveAsync(ctx.TenantId, Arg.Any<Sku>(), 3, orderId, Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(newId));

        var result = await handler.Handle(
            new ReserveStockCommand(orderId, "SKU-001", 3),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(newId);
    }

    [Fact]
    public async Task Handle_WhenTryReserveFailsOversold_PassesFailureThrough()
    {
        var (handler, repo, ctx) = MakeHandler();
        var orderId = Guid.NewGuid();
        repo.FindByOrderIdAsync(ctx.TenantId, orderId, Arg.Any<CancellationToken>())
            .Returns((Reservation?)null);
        repo.TryReserveAsync(
                ctx.TenantId,
                Arg.Any<Sku>(),
                Arg.Any<int>(),
                orderId,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result<Guid>.Failure("oversold", "OVERSOLD"));

        var result = await handler.Handle(
            new ReserveStockCommand(orderId, "SKU-001", 100),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("OVERSOLD");
    }
}
