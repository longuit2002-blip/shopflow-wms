using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Outbound.Api.Contracts;
using ShopFlow.Outbound.Api.Controllers;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.Outbound.Infrastructure.Outbox;
using ShopFlow.Outbound.Infrastructure.Repositories;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Outbound.IntegrationTests;

/// <summary>
/// Sprint-3-redux U2 — <see cref="OrdersController"/> against real Postgres.
/// Exercises the controller directly (no <c>WebApplicationFactory</c>) —
/// the controller is thin enough that direct instantiation matches the
/// HTTP shape covered in the plan's U2 test scenarios while keeping the
/// harness minimal. The four saga-driving endpoints are 501 stubs so
/// they're not in scope here; U6/U7 add their behaviour tests.
/// </summary>
[Collection(OutboundTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OrdersControllerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 5, 13, 10, 0, 0, TimeSpan.Zero);

    private readonly OutboundTenantFixture _fx;
    private ProvisionedOutboundTenant _tenant = default!;

    public OrdersControllerTests(OutboundTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("orders-ctl");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PostOrders_HappyPath_Returns201WithLocationAndPersistsOrderAndOutboxRow()
    {
        var harness = BuildHarness();
        var request = new CreateOrderRequest(
            ChannelExternalOrderId: "ext-create-1",
            ShippingProfile: "standard",
            Lines: new[]
            {
                new CreateOrderLineRequest("SKU-A", 2, 100),
                new CreateOrderLineRequest("SKU-B", 5, 50),
            }
        );

        var result = await harness.Controller.CreateAsync(request, CancellationToken.None);

        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(OrdersController.GetByIdAsync));
        var body = created.Value.Should().BeOfType<OrderResponse>().Subject;
        body.ChannelExternalOrderId.Should().Be("ext-create-1");
        body.Status.Should().Be("Created");
        body.ExpectedWeightTotal.Should().Be(450);
        body.Lines.Should().HaveCount(2);

        // Verify order row + outbox row landed in the same physical DB.
        await using var verify = new OutboundDbContext(_tenant.Options);
        var orderRow = await verify.Orders.Include(o => o.Lines).SingleAsync();
        orderRow.Id.Should().Be(body.Id);
        orderRow.Lines.Should().HaveCount(2);

        var outboxRows = await verify.OutboxMessages.AsNoTracking().ToListAsync();
        outboxRows.Should().HaveCount(1);
        var row = outboxRows.Single();
        row.EventType.Should().StartWith("ShopFlow.Contracts.Outbound.OrderPlacedV1");
        row.TenantId.Should().Be(_tenant.Info.Id);
        // Confirm the payload uses camelCase per OutboxJsonOptions.Default
        // and contains the order id — the wire-format contract is what
        // U3's canonical contract type must match.
        using var doc = JsonDocument.Parse(row.Payload);
        doc.RootElement.GetProperty("orderId").GetGuid().Should().Be(body.Id);
        doc.RootElement.GetProperty("channelExternalOrderId").GetString().Should().Be("ext-create-1");
        doc.RootElement.GetProperty("shippingProfile").GetString().Should().Be("standard");
        doc.RootElement.GetProperty("lines").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task PostOrders_DuplicateChannelExternalOrderId_ReturnsSameOrderIdWithSingleRow()
    {
        var harness = BuildHarness();
        var request = new CreateOrderRequest(
            ChannelExternalOrderId: "ext-idem-1",
            ShippingProfile: "standard",
            Lines: new[] { new CreateOrderLineRequest("SKU-A", 1, null) }
        );

        var first = await harness.Controller.CreateAsync(request, CancellationToken.None);
        var firstBody = first
            .Should()
            .BeOfType<CreatedAtActionResult>()
            .Subject.Value.Should()
            .BeOfType<OrderResponse>()
            .Subject;

        // Build a fresh controller scope to simulate a second request.
        var harness2 = BuildHarness();
        var second = await harness2.Controller.CreateAsync(request, CancellationToken.None);
        var secondBody = second
            .Should()
            .BeOfType<OkObjectResult>()
            .Subject.Value.Should()
            .BeOfType<OrderResponse>()
            .Subject;

        secondBody.Id.Should().Be(firstBody.Id);

        await using var verify = new OutboundDbContext(_tenant.Options);
        var rowCount = await verify
            .Orders.CountAsync(o => o.ChannelExternalOrderId == "ext-idem-1");
        rowCount.Should().Be(1);
    }

    [Fact]
    public async Task PostOrders_EmptyLines_Returns400WithCode()
    {
        var harness = BuildHarness();
        var request = new CreateOrderRequest(
            ChannelExternalOrderId: "ext-bad-empty",
            ShippingProfile: "standard",
            Lines: Array.Empty<CreateOrderLineRequest>()
        );

        var result = await harness.Controller.CreateAsync(request, CancellationToken.None);

        AssertProblemWithCode(result, expectedStatus: 400, expectedCode: "order.no_lines");
    }

    [Fact]
    public async Task PostOrders_NonPositiveQty_Returns400WithCode()
    {
        var harness = BuildHarness();
        var request = new CreateOrderRequest(
            ChannelExternalOrderId: "ext-bad-qty",
            ShippingProfile: "standard",
            Lines: new[] { new CreateOrderLineRequest("SKU-A", 0, null) }
        );

        var result = await harness.Controller.CreateAsync(request, CancellationToken.None);

        AssertProblemWithCode(
            result,
            expectedStatus: 400,
            expectedCode: "order_line.qty_non_positive"
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PostOrders_BlankExternalId_Returns400WithCode(string externalId)
    {
        var harness = BuildHarness();
        var request = new CreateOrderRequest(
            ChannelExternalOrderId: externalId,
            ShippingProfile: "standard",
            Lines: new[] { new CreateOrderLineRequest("SKU-A", 1, null) }
        );

        var result = await harness.Controller.CreateAsync(request, CancellationToken.None);

        AssertProblemWithCode(
            result,
            expectedStatus: 400,
            expectedCode: "order.external_id_required"
        );
    }

    [Fact]
    public async Task GetOrder_Unknown_Returns404()
    {
        var harness = BuildHarness();

        var result = await harness.Controller.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        AssertProblemWithCode(result, expectedStatus: 404, expectedCode: "order.not_found");
    }

    [Fact]
    public async Task GetOrder_Existing_Returns200WithOrderShape()
    {
        var harness = BuildHarness();
        var createRequest = new CreateOrderRequest(
            ChannelExternalOrderId: "ext-get-1",
            ShippingProfile: "standard",
            Lines: new[] { new CreateOrderLineRequest("SKU-A", 1, 100) }
        );
        var createResult = await harness.Controller.CreateAsync(
            createRequest,
            CancellationToken.None
        );
        var createdBody = createResult
            .Should()
            .BeOfType<CreatedAtActionResult>()
            .Subject.Value.Should()
            .BeOfType<OrderResponse>()
            .Subject;

        // Fresh scope so we don't hit EF's identity-map cache.
        var harness2 = BuildHarness();
        var getResult = await harness2.Controller.GetByIdAsync(
            createdBody.Id,
            CancellationToken.None
        );

        var body = getResult
            .Should()
            .BeOfType<OkObjectResult>()
            .Subject.Value.Should()
            .BeOfType<OrderResponse>()
            .Subject;

        body.Id.Should().Be(createdBody.Id);
        body.Status.Should().Be("Created");
        body.Lines.Should().HaveCount(1);
        body.Lines[0].Sku.Should().Be("SKU-A");
        body.Lines[0].Qty.Should().Be(1);
        body.Lines[0].ExpectedWeight.Should().Be(100);
    }

    /// <summary>
    /// Builds one controller scope: a fresh <see cref="OutboundDbContext"/> +
    /// repository + UoW + outbox + a stub <see cref="IRequestContext"/>
    /// bound to the provisioned tenant. Mirrors the per-request scope the
    /// real DI pipeline produces; tests reach into the controller directly
    /// to keep the harness minimal (no <c>WebApplicationFactory</c>).
    /// </summary>
    private ControllerHarness BuildHarness()
    {
        var db = new OutboundDbContext(_tenant.Options);
        var rc = _tenant.BuildRequestContext();
        var outbox = new OutboundOutbox(db, rc, new FakeClock(FixedNow));
        var controller = new OrdersController(
            orderRepo: new OrderRepository(db),
            uow: new OutboundUnitOfWork(db),
            outbox: outbox,
            clock: new FakeClock(FixedNow)
        );
        return new ControllerHarness(controller, db);
    }

    private static void AssertProblemWithCode(
        IActionResult actionResult,
        int expectedStatus,
        string expectedCode
    )
    {
        var problem = actionResult.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(expectedStatus);
        var details = problem.Value.Should().BeAssignableTo<ProblemDetails>().Subject;
        details.Type.Should().Be($"https://shopflow.example/errors/{expectedCode}");
    }

    private sealed record ControllerHarness(OrdersController Controller, OutboundDbContext Db)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }

    private sealed class FakeClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FakeClock(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
