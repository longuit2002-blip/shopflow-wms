using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Outbound.Application.Sagas;
using ShopFlow.Outbound.Domain;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.TestSupport;

namespace ShopFlow.Outbound.IntegrationTests.Handoff;

/// <summary>
/// Sprint-12 / Sprint-13 / finish-line U4 — Docker-backed end-to-end happy-path
/// proofs that real role JWTs, over real HTTP, drive ONE fulfillment saga to
/// Shipped. Completes the hand-off half of AE4 (the denial half is
/// <see cref="CrossRoleDenialTests"/>, 14/14 green).
///
/// <para><b>Finish-line U4 — the saga drive-through wiring (bug 6).</b> The
/// confirm-pick/pack/ship endpoints publish their saga events directly via
/// <c>IPublishEndpoint</c> (not the outbox, which stamps the tenant id). The
/// saga consume scope runs OUTSIDE the HTTP request scope, so two pieces had to
/// be wired for the per-tenant binding to reach the saga: (1) the controller
/// now stamps the <c>tenant_id</c> envelope header on those publishes
/// (<c>OrdersController.PublishWithTenantAsync</c>), and (2)
/// <c>ConfigureOutboundBus</c> attaches <c>TenantBindingSagaFilter&lt;&gt;</c> to
/// the receive endpoints (via <c>AddConfigureEndpointsCallback</c>, inside the
/// kernel's single <c>AddMassTransit</c>). The filter reads the header, resolves
/// the tenant, and binds <c>RequestContext</c> so the saga repository's
/// <c>OutboundDbContext</c> lands in the right per-tenant DB.</para>
///
/// <para><b>Mid-flow seed.</b> The saga is normally created on
/// <c>OrderPlacedV1</c>; these proofs enter mid-flow, so the fixture seeds the
/// saga instance + the order row directly at <c>AwaitingPick</c> (the
/// <c>PickConfirmed</c> event correlates to it by OrderId). The order row is the
/// operator-facing state the controller's pre-state guards read; the saga is the
/// authoritative state for cross-module commands. They run a step apart by
/// design (Sprint-12 KTD2: the Order aggregate reaches AwaitingShip on
/// confirm-pack while the saga sits at Packed).</para>
///
/// <para>Gated behind <see cref="ProofFactAttribute"/> — run via
/// <c>task proofs</c> (or CI), skipped on a default no-Docker run.</para>
/// </summary>
[Collection(HandoffCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Category", "Proof")]
public sealed class HandoffWorkflowTests
{
    private const string OrdersBase = "/api/outbound/orders";

    private readonly HandoffFixture _fixture;

    public HandoffWorkflowTests(HandoffFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Sprint-12 3-role chain: Picker confirms pick → Owner confirms pack →
    /// Dispatcher confirms ship. Owner-as-Packer is the pre-Sprint-13 path
    /// (Owner retains pack-confirm under the ADDITIVE-ONLY contract), kept as
    /// regression coverage alongside the 4-role variant.
    /// </summary>
    [ProofFact]
    public async Task HappyPath_AllThreeRolesDriveSagaToShipped()
    {
        var orderId = await SeedOrderAtAwaitingPickAsync();

        // STEP 1 — Picker confirm-pick → saga AwaitingPick → Picked.
        (await Post(_fixture.BuildPickerJwt(), $"{OrdersBase}/{orderId}/confirm-pick"))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
        await PollSagaStateAsync(orderId, "Picked");

        // STEP 2 — Owner confirm-pack → saga Picked → Packed; order row chains
        // Packed → AwaitingShip in the same SaveChanges (KTD2).
        (
            await PostJson(
                _fixture.BuildOwnerJwt(),
                $"{OrdersBase}/{orderId}/confirm-pack",
                new { actualWeightTotal = 100 }
            )
        )
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
        await PollSagaStateAsync(orderId, "Packed");
        (await ReadOrderStatusAsync(orderId)).Should().Be(OrderStatus.AwaitingShip);

        // STEP 3 — Dispatcher confirm-ship → saga Packed → Shipped; order
        // Shipped + tracking persisted.
        (await Post(_fixture.BuildDispatcherJwt(), $"{OrdersBase}/{orderId}/confirm-ship"))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
        await PollSagaStateAsync(orderId, "Shipped");
        (await ReadOrderStatusAsync(orderId)).Should().Be(OrderStatus.Shipped);
    }

    /// <summary>
    /// Sprint-13 4-role chain: Picker → <b>Packer</b> → Dispatcher. Pack-confirm
    /// moves off Owner to the Packer baseline; Owner is never used. Proves the
    /// Packer baseline's <c>outbound.orders.pack-confirm</c> key authorizes the
    /// pack transition on a real saga drive-through.
    /// </summary>
    [ProofFact]
    public async Task HappyPath_AllFourRoles_DriveSagaToShipped()
    {
        var orderId = await SeedOrderAtAwaitingPickAsync();

        // STEP 1 — Picker confirm-pick.
        (await Post(_fixture.BuildPickerJwt(), $"{OrdersBase}/{orderId}/confirm-pick"))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
        await PollSagaStateAsync(orderId, "Picked");

        // STEP 2 — PACKER confirm-pack (not Owner).
        (
            await PostJson(
                _fixture.BuildPackerJwt(),
                $"{OrdersBase}/{orderId}/confirm-pack",
                new { actualWeightTotal = 100 }
            )
        )
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
        await PollSagaStateAsync(orderId, "Packed");
        (await ReadOrderStatusAsync(orderId)).Should().Be(OrderStatus.AwaitingShip);

        // STEP 3 — Dispatcher confirm-ship.
        (await Post(_fixture.BuildDispatcherJwt(), $"{OrdersBase}/{orderId}/confirm-ship"))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
        await PollSagaStateAsync(orderId, "Shipped");
        (await ReadOrderStatusAsync(orderId)).Should().Be(OrderStatus.Shipped);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private HttpClient Authed(string jwt)
    {
        var client = _fixture.HttpClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return client;
    }

    private Task<HttpResponseMessage> Post(string jwt, string path) =>
        Authed(jwt).PostAsync(path, content: null);

    private Task<HttpResponseMessage> PostJson(string jwt, string path, object body) =>
        Authed(jwt).PostAsync(path, JsonContent.Create(body));

    /// <summary>
    /// Seed the order row (via the Order aggregate, then flipped to
    /// AwaitingPick) AND the saga instance at AwaitingPick (via the mapped
    /// FulfillmentSagaState entity — no raw-SQL column guessing). The
    /// PickConfirmed event correlates to this saga row by OrderId.
    /// </summary>
    private async Task<Guid> SeedOrderAtAwaitingPickAsync()
    {
        var options = new DbContextOptionsBuilder<OutboundDbContext>()
            .UseNpgsql(_fixture.TenantConnectionString)
            .Options;

        Guid orderId;
        await using (var db = new OutboundDbContext(options))
        {
            var created = Order.Create(
                channelExternalOrderId: $"ext-{Guid.NewGuid():N}",
                shippingProfile: "standard",
                lines: new[] { ("SKU-A", 1, (int?)100) }
            );
            created.IsSuccess.Should().BeTrue(created.Error);
            var order = created.Value!;
            db.Orders.Add(order);

            db.Set<FulfillmentSagaState>()
                .Add(
                    new FulfillmentSagaState
                    {
                        CorrelationId = order.Id,
                        CurrentState = "AwaitingPick",
                        RowVersion = Array.Empty<byte>(),
                        UpdatedAt = DateTime.UtcNow,
                        Version = 1,
                        TenantId = Guid.NewGuid(),
                        ShippingProfile = "standard",
                        LineCount = 1,
                        ReservedLineSkus = string.Empty,
                        ReleasedLineSkus = string.Empty,
                        LinesAwaitingRelease = 0,
                    }
                );

            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        // Order.Create lands in Created; flip to AwaitingPick (state-machine
        // guards bypassed for the seed — Sprint-11 U3 + CrossRoleDenialTests
        // precedent).
        await using (var conn = new NpgsqlConnection(_fixture.TenantConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE orders SET status = 'AwaitingPick' WHERE id = @id";
            cmd.Parameters.AddWithValue("id", orderId);
            (await cmd.ExecuteNonQueryAsync()).Should().Be(1);
        }

        return orderId;
    }

    private async Task PollSagaStateAsync(Guid orderId, string expected, int timeoutSeconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        string? last = null;
        while (DateTime.UtcNow < deadline)
        {
            await using var conn = new NpgsqlConnection(_fixture.TenantConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """SELECT "CurrentState" FROM saga_state WHERE "CorrelationId" = @id""";
            cmd.Parameters.AddWithValue("id", orderId);
            last = (string?)await cmd.ExecuteScalarAsync();
            if (last == expected)
            {
                return;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException(
            $"saga {orderId} did not reach {expected} within {timeoutSeconds}s (last={last ?? "<null>"})."
        );
    }

    private async Task<OrderStatus> ReadOrderStatusAsync(Guid orderId)
    {
        await using var conn = new NpgsqlConnection(_fixture.TenantConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM orders WHERE id = @id";
        cmd.Parameters.AddWithValue("id", orderId);
        var raw = (string?)await cmd.ExecuteScalarAsync();
        raw.Should().NotBeNull("seeded order must exist");
        return Enum.Parse<OrderStatus>(raw!);
    }
}
