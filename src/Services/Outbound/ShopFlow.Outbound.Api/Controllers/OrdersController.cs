using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using ShopFlow.Contracts.Inventory;
using ShopFlow.Contracts.Outbound;
using ShopFlow.Outbound.Api.Contracts;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Application.Queries;
using ShopFlow.Outbound.Application.Sagas.Events;
using ShopFlow.Outbound.Domain;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Attributes;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Outbound.Api.Controllers;

/// <summary>
/// Operator-facing HTTP surface for the order fulfillment flow per
/// Sprint-3-redux plan R11. Controllers stay thin: validate input,
/// drive the Domain aggregate, map <see cref="Result"/> to HTTP status
/// via <c>ProblemDetails</c> on failure. Mirrors Sprint-2-redux's
/// <c>PurchaseOrdersController</c>.
/// </summary>
/// <remarks>
/// <para>U2 wires the manual <c>POST /api/outbound/orders</c> (with
/// idempotent <c>channel_external_order_id</c>) + <c>GET /api/outbound/orders/{id}</c>.
/// U6 ships the three saga-driving endpoints
/// (<c>POST /{id}/confirm-pick</c>, <c>POST /{id}/confirm-pack</c>,
/// <c>POST /{id}/confirm-ship</c>); U7 wires the
/// <c>POST /{id}/mark-pick-failed</c> compensation entry.</para>
///
/// <para>The Create flow stamps the order as
/// <see cref="OrderStatus.Created"/> and enqueues a stub
/// <c>OrderPlacedV1</c> payload to <c>outbound_outbox_messages</c>
/// (atomic with the order insert). The dispatcher (U1) drains the
/// outbox; the saga (U4) consumes <c>OrderPlacedV1</c> on the bus and
/// drives the order forward.</para>
///
/// <para>U6's confirm-pick / confirm-pack / confirm-ship actions follow
/// the R3 eventual-consistency boundary: the controller's
/// <see cref="IUnitOfWork.SaveChangesAsync"/> commits the order +
/// outbox rows in one EF transaction; the in-process saga event
/// (<see cref="PickConfirmed"/> / <see cref="PackConfirmed"/> /
/// <see cref="ShipConfirmed"/>) is published via
/// <see cref="IPublishEndpoint"/> and the saga's state-machine commit
/// lands in a separate MassTransit transaction.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/outbound/orders")]
public sealed class OrdersController : ControllerBase
{
    /// <summary>
    /// Sprint-7 U4 — Idempotency-Key header name. Logged on the seed
    /// endpoint for audit (Sprint-6 trade-off #2: no dedup table; the
    /// natural <c>UNIQUE(channel_external_order_id)</c> on the orders
    /// row is the real defence).
    /// </summary>
    private const string IdempotencyHeader = "Idempotency-Key";

    /// <summary>
    /// Sprint-7 U4 — defensive ceiling on the seed endpoint's
    /// <c>line_count</c> argument so a malicious payload cannot grow an
    /// order to thousands of lines on a dev container.
    /// </summary>
    private const int MaxSeedLineCount = 50;

    /// <summary>
    /// Sprint-7 U4 — default page size for <c>GET /api/outbound/orders</c>.
    /// </summary>
    private const int DefaultListTake = 50;

    /// <summary>
    /// Weight-variance threshold above which <c>confirm-pack</c> flags a
    /// warning. Per the U6 plan spec: |actual - expected| / expected &gt; 10%.
    /// </summary>
    public const double WeightWarningThreshold = 0.10;

    /// <summary>
    /// Canonical wire-format event type for <c>OrderPlacedV1</c>. Sprint-3-redux
    /// U3 landed the contract type; this is its assembly-qualified name
    /// (the form the dispatcher's <c>Type.GetType</c> reads at dispatch
    /// time).
    /// </summary>
    internal static readonly string OrderPlacedV1EventType =
        typeof(OrderPlacedV1).AssemblyQualifiedName!;

    /// <summary>
    /// Wire-format event type for <c>ConfirmStockV1</c> — emitted on the
    /// AwaitingShip → Shipped transition so the Inventory module's
    /// <c>ConfirmStockConsumer</c> can drain Pending reservations on the
    /// confirmed order. Per K13 dispatcher uses Publish for all envelopes
    /// today; W6 mechanical split adds envelope-type → endpoint routing.
    /// </summary>
    internal static readonly string ConfirmStockV1EventType =
        typeof(ConfirmStockV1).AssemblyQualifiedName!;

    /// <summary>
    /// Wire-format event type for <c>TrackingPushedV1</c> — consumed by
    /// the stub <c>ChannelTrackingConsumer</c> in Sprint-3-redux (Phase-2
    /// Sprint-4 moves the consumer to <c>ShopFlow.Channel.Infrastructure</c>).
    /// </summary>
    internal static readonly string TrackingPushedV1EventType =
        typeof(TrackingPushedV1).AssemblyQualifiedName!;

    private readonly IOrderRepository _orderRepo;
    private readonly IUnitOfWork _uow;
    private readonly IOutboundOutbox _outbox;
    private readonly IRequestContext _requestContext;
    private readonly TimeProvider _clock;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IMockShippingProvider _shippingProvider;
    // Sprint-7 U4 — MediatR powers the read endpoints (list / detail /
    // transitions) per the Sprint-6 Inventory controller pattern. The
    // handlers (U3) live in ShopFlow.Outbound.Application and are wired
    // through AddShopFlowDefaults' MediatR assembly scan (see
    // Program.cs — Outbound.Application is added to assembliesToScan).
    private readonly IMediator _mediator;
    // Sprint-7 U4 — IHostEnvironment gates POST /seed to development
    // only. IsDevelopment() returns true when ASPNETCORE_ENVIRONMENT is
    // "Development"; production / staging boots return 404 +
    // environment_not_dev.
    private readonly IHostEnvironment _env;

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public OrdersController(
        IOrderRepository orderRepo,
        IUnitOfWork uow,
        IOutboundOutbox outbox,
        IRequestContext requestContext,
        TimeProvider clock,
        IPublishEndpoint publishEndpoint,
        IMockShippingProvider shippingProvider,
        IMediator mediator,
        IHostEnvironment env
    )
    {
        _orderRepo = orderRepo;
        _uow = uow;
        _outbox = outbox;
        _requestContext = requestContext;
        _clock = clock;
        _publishEndpoint = publishEndpoint;
        _shippingProvider = shippingProvider;
        _mediator = mediator;
        _env = env;
    }

    /// <summary>
    /// Sprint-7 U4 — backward-compat constructor for the pre-Sprint-7
    /// integration tests that wire the controller directly (no DI).
    /// The legacy POST + GET + confirm-pick/pack/ship + mark-pick-failed
    /// endpoints don't touch <see cref="IMediator"/> or
    /// <see cref="IHostEnvironment"/>; sub-stubs satisfy the field
    /// initializers so the existing test harnesses don't have to thread
    /// new dependencies through. DI always picks the 9-arg primary ctor
    /// at runtime because both stubbed services are registered through
    /// <c>AddShopFlowDefaults</c>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1062",
        Justification = "Test-only ctor; DI selects the 9-arg primary ctor at runtime."
    )]
    public OrdersController(
        IOrderRepository orderRepo,
        IUnitOfWork uow,
        IOutboundOutbox outbox,
        IRequestContext requestContext,
        TimeProvider clock,
        IPublishEndpoint publishEndpoint,
        IMockShippingProvider shippingProvider
    )
        : this(
            orderRepo,
            uow,
            outbox,
            requestContext,
            clock,
            publishEndpoint,
            shippingProvider,
            mediator: new LegacyTestUnsupportedMediator(),
            env: new LegacyTestHostEnvironment()
        )
    {
    }

    /// <summary>
    /// Sprint-7 U4 — placeholder injected by the legacy test-only
    /// constructor. Throws if a pre-Sprint-7 test inadvertently calls
    /// one of the new Sprint-7 read endpoints; existing tests never
    /// touched the new endpoints so this stays unreached in normal use.
    /// </summary>
    private sealed class LegacyTestUnsupportedMediator : IMediator
    {
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default
        ) => throw NotSupported();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default
        ) => throw NotSupported();

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => throw NotSupported();

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default
        )
            where TNotification : INotification => throw NotSupported();

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default
        ) => throw NotSupported();

        public Task<object?> Send(
            object request,
            CancellationToken cancellationToken = default
        ) => throw NotSupported();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw NotSupported();

        private static InvalidOperationException NotSupported() =>
            new("Sprint-7 U4 — legacy test-only constructor: MediatR is not available. "
                + "Use the 9-arg constructor to exercise the Sprint-7 list / detail / "
                + "transitions / seed endpoints.");
    }

    /// <summary>
    /// Sprint-7 U4 — placeholder env reporting <c>"Test"</c>. Legacy
    /// tests never reach <see cref="SeedAsync"/>, so the
    /// <c>IsDevelopment()</c> path is unused; if reached the env is
    /// "Test" (not "Development"), which trips the 404 guard.
    /// </summary>
    private sealed class LegacyTestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "ShopFlow.Outbound.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider
        {
            get; set;
        } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    [HttpPost]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateOrderRequest request,
        CancellationToken ct
    )
    {
        if (request is null)
        {
            return ProblemFromError("request body is required.", "order.request_required", 400);
        }

        // Idempotency short-circuit: same channel_external_order_id twice
        // returns the existing order. The UNIQUE index on the column
        // (plan R1) is defence in depth against a concurrent race.
        if (!string.IsNullOrWhiteSpace(request.ChannelExternalOrderId))
        {
            var existing = await _orderRepo
                .FindByExternalIdAsync(request.ChannelExternalOrderId.Trim(), ct)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                return Ok(Map(existing));
            }
        }

        var orderResult = Order.Create(
            request.ChannelExternalOrderId,
            request.ShippingProfile,
            request.Lines?.Select(l => (l.Sku, l.Qty, l.ExpectedWeight))
                ?? Array.Empty<(string, int, int?)>()
        );
        if (!orderResult.IsSuccess)
        {
            return ProblemFromResult(orderResult.Error!, orderResult.ErrorCode!);
        }

        var order = orderResult.Value!;
        await _orderRepo.AddAsync(order, ct).ConfigureAwait(false);

        // Sprint-3-redux U3: use the canonical OrderPlacedV1 contract type.
        // The wire-format JSON is unchanged from U2's anonymous stub —
        // OutboxJsonOptions.Default's camelCase naming + identical field
        // set means downstream consumers see the same bytes.
        var placedAt = _clock.GetUtcNow().UtcDateTime;
        var placedPayload = new OrderPlacedV1(
            OrderId: order.Id,
            TenantId: _requestContext.TenantId,
            ChannelExternalOrderId: order.ChannelExternalOrderId,
            ShippingProfile: order.ShippingProfile,
            Lines: order
                .Lines.Select(l => new OrderPlacedLineV1(
                    OrderLineId: l.Id.ToString(),
                    Sku: l.Sku,
                    Qty: l.Qty,
                    ExpectedWeight: l.ExpectedWeight
                ))
                .ToArray(),
            OccurredAt: placedAt
        );
        await _outbox
            .AppendAsync(OrderPlacedV1EventType, placedPayload, ct)
            .ConfigureAwait(false);

        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);

        return CreatedAtAction(nameof(GetByIdAsync), new { id = order.Id }, Map(order));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var order = await _orderRepo.FindByIdAsync(id, ct).ConfigureAwait(false);
        if (order is null)
        {
            return ProblemFromError($"order {id} not found.", "order.not_found", 404);
        }
        return Ok(Map(order));
    }

    // ── Sprint-7 U4 — Orders screen read + seed endpoints ───────────────
    // All four wire through TenantRoutingMiddleware (JWT tenant_slug
    // claim → per-tenant DbContext). Wire-shape stays PascalCase per
    // Sprint-6 KTD4.

    /// <summary>
    /// Sprint-7 U4 — paginated list for the Orders screen
    /// (<c>GET /api/outbound/orders</c>). Delegates to
    /// <see cref="ListOrdersQuery"/> (U3); the handler joins
    /// <c>outbound_saga_transitions</c> for the per-row last-update
    /// timestamp in a single trip.
    /// </summary>
    /// <remarks>
    /// Query knobs:
    /// <list type="bullet">
    ///   <item><description><c>status</c> — case-sensitive enum-name equality on <c>orders.status</c>.</description></item>
    ///   <item><description><c>channel</c> — case-sensitive prefix match on <c>channel_external_order_id</c> (forwarded verbatim).</description></item>
    ///   <item><description><c>search</c> — case-insensitive substring match on <c>channel_external_order_id</c>.</description></item>
    ///   <item><description><c>since</c> / <c>until</c> — ISO 8601 bounds on <c>orders.created_at</c>; invalid formats → 400.</description></item>
    ///   <item><description><c>skip</c> / <c>take</c> — paging knobs; the handler clamps <c>take</c> to 200.</description></item>
    /// </list>
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(OrderListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] string? status,
        [FromQuery] string? channel,
        [FromQuery] string? search,
        [FromQuery] string? since,
        [FromQuery] string? until,
        [FromQuery] int skip = 0,
        [FromQuery] int take = DefaultListTake,
        CancellationToken ct = default
    )
    {
        DateTime? sinceParsed = null;
        DateTime? untilParsed = null;
        if (!string.IsNullOrWhiteSpace(since))
        {
            if (!DateTime.TryParse(
                    since,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind
                        | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var parsed))
            {
                return ProblemFromError(
                    $"since '{since}' is not a valid ISO 8601 timestamp.",
                    "order.invalid_since",
                    400
                );
            }
            sinceParsed = parsed.ToUniversalTime();
        }
        if (!string.IsNullOrWhiteSpace(until))
        {
            if (!DateTime.TryParse(
                    until,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind
                        | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var parsed))
            {
                return ProblemFromError(
                    $"until '{until}' is not a valid ISO 8601 timestamp.",
                    "order.invalid_until",
                    400
                );
            }
            untilParsed = parsed.ToUniversalTime();
        }

        if (skip < 0)
        {
            return ProblemFromError(
                "skip must be non-negative.",
                "order.invalid_skip",
                400
            );
        }
        if (take < 1)
        {
            return ProblemFromError(
                "take must be at least 1.",
                "order.invalid_take",
                400
            );
        }

        var filter = new OrderListFilter(
            Status: string.IsNullOrWhiteSpace(status) ? null : status,
            ChannelPrefix: string.IsNullOrWhiteSpace(channel) ? null : channel,
            Search: string.IsNullOrWhiteSpace(search) ? null : search,
            Since: sinceParsed,
            Until: untilParsed
        );

        var page = await _mediator
            .Send(new ListOrdersQuery(filter, skip, take), ct)
            .ConfigureAwait(false);

        var now = _clock.GetUtcNow().UtcDateTime;
        var items = page.Items
            .Select(r => new OrderListItemDto(
                Id: r.Id,
                ChannelExternalOrderId: r.ChannelExternalOrderId,
                Channel: r.Channel,
                LineCount: r.LineCount,
                CurrentSagaState: r.CurrentSagaState,
                Age: now - r.CreatedAt,
                LastTransitionAt: r.LastTransitionAt
            ))
            .ToList();

        return Ok(new OrderListResponse(items, page.TotalCount));
    }

    /// <summary>
    /// Sprint-7 U4 — KPI strip aggregate for the Orders screen
    /// (<c>GET /api/outbound/orders/kpis</c>). Returns four counts:
    /// active orders (non-terminal), AwaitingPick, AwaitingShip, and
    /// "failed today" (Cancelled rows with <c>created_at</c> ≥ start-of-
    /// UTC-today). Implemented as four <see cref="ListOrdersQuery"/>
    /// dispatches over <c>TotalCount</c> only — the per-row Items payload
    /// is discarded. Cleanest path that re-uses the existing handler +
    /// repo infrastructure without bolting a separate query type onto U3.
    /// </summary>
    [HttpGet("kpis")]
    [ProducesResponseType(typeof(OrderKpiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetKpisAsync(CancellationToken ct)
    {
        var startOfTodayUtc = _clock.GetUtcNow().UtcDateTime.Date;

        // Serialize the six reads — EF Core's scoped DbContext is single-
        // threaded and Task.WhenAll on the shared scope races the same
        // DbCommand. The handler asks for one row + reads TotalCount so
        // each call is a single COUNT(*) plus a 1-row materialise; total
        // wall-time is acceptable for the KPI strip's 2-s polling cadence.
        var awaitingPick = (await _mediator.Send(
            new ListOrdersQuery(
                new OrderListFilter(Status: nameof(OrderStatus.AwaitingPick)),
                Skip: 0, Take: 1), ct).ConfigureAwait(false)).TotalCount;
        var awaitingShip = (await _mediator.Send(
            new ListOrdersQuery(
                new OrderListFilter(Status: nameof(OrderStatus.AwaitingShip)),
                Skip: 0, Take: 1), ct).ConfigureAwait(false)).TotalCount;
        var failedToday = (await _mediator.Send(
            new ListOrdersQuery(
                new OrderListFilter(
                    Status: nameof(OrderStatus.Cancelled),
                    Since: startOfTodayUtc),
                Skip: 0, Take: 1), ct).ConfigureAwait(false)).TotalCount;

        // Active = total - shipped - cancelled. Three reads against the
        // status-indexed orders table; the SQL planner picks the right
        // path for each filter.
        var total = (await _mediator.Send(
            new ListOrdersQuery(new OrderListFilter(), Skip: 0, Take: 1),
            ct).ConfigureAwait(false)).TotalCount;
        var shipped = (await _mediator.Send(
            new ListOrdersQuery(
                new OrderListFilter(Status: nameof(OrderStatus.Shipped)),
                Skip: 0, Take: 1), ct).ConfigureAwait(false)).TotalCount;
        var cancelled = (await _mediator.Send(
            new ListOrdersQuery(
                new OrderListFilter(Status: nameof(OrderStatus.Cancelled)),
                Skip: 0, Take: 1), ct).ConfigureAwait(false)).TotalCount;
        var active = Math.Max(0, total - shipped - cancelled);

        return Ok(new OrderKpiResponse(
            ActiveOrders: active,
            AwaitingPick: awaitingPick,
            AwaitingShip: awaitingShip,
            FailedToday: failedToday
        ));
    }

    /// <summary>
    /// Sprint-7 U4 — audit log for one order
    /// (<c>GET /api/outbound/orders/{id}/transitions</c>). Returns the
    /// rows in <c>occurred_at</c> ASC order so the frontend renders them
    /// top-to-bottom chronologically. Empty list when the saga has not
    /// produced any transitions yet — the handler intentionally does NOT
    /// 404 on unknown order ids (the audit is independent of the orders
    /// table per Sprint-7 R14).
    /// </summary>
    [HttpGet("{id:guid}/transitions")]
    [ProducesResponseType(typeof(IReadOnlyList<OrderTransitionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTransitionsAsync(Guid id, CancellationToken ct)
    {
        var rows = await _mediator
            .Send(new GetOrderTransitionsQuery(id), ct)
            .ConfigureAwait(false);

        var dtos = rows
            .Select(r => new OrderTransitionDto(
                Id: r.Id,
                OrderId: r.OrderId,
                FromState: r.FromState,
                ToState: r.ToState,
                OccurredAt: r.OccurredAt,
                EventType: r.EventType,
                CorrelationId: r.CorrelationId
            ))
            .ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Sprint-7 U4 — dev-mode seed endpoint
    /// (<c>POST /api/outbound/orders/seed</c>). Creates an
    /// <see cref="Order"/> with N synthesized lines (default 3) +
    /// emits <c>OrderPlacedV1</c> via the same outbox path the
    /// <see cref="CreateAsync"/> endpoint uses, so the saga starts
    /// naturally on the next dispatcher tick. Returns <c>404</c> +
    /// <c>environment_not_dev</c> outside Development.
    /// </summary>
    /// <remarks>
    /// <para>Per AGENTS.md §6.40 the endpoint is tagged
    /// <see cref="IdempotentAttribute"/> — the <c>Idempotency-Key</c>
    /// header value is read into the audit logs (Sprint-6 trade-off #2:
    /// no <c>inventory_idempotency_records</c> table; the natural
    /// <c>UNIQUE(channel_external_order_id)</c> on the orders row is
    /// the real dedup defence).</para>
    ///
    /// <para>Seed defaults per plan: lines are <c>SEED-SKU-1</c>,
    /// <c>SEED-SKU-2</c>, … each qty=1, expectedWeight=100,
    /// shippingProfile="standard". The channel-external-order-id is
    /// <c>{prefix}{ulid-ish-suffix}</c> where the suffix is a fresh
    /// timestamp + GUID prefix so repeated calls don't collide on the
    /// UNIQUE index.</para>
    /// </remarks>
    [HttpPost("seed")]
    [Idempotent]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SeedAsync(
        [FromBody] SeedOrderRequest? request,
        [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
        CancellationToken ct
    )
    {
        if (!_env.IsDevelopment())
        {
            return ProblemFromError(
                "POST /seed is only available in Development environments.",
                "environment_not_dev",
                404
            );
        }

        request ??= new SeedOrderRequest();
        var lineCount = Math.Clamp(request.LineCount, 1, MaxSeedLineCount);
        var prefix = string.IsNullOrWhiteSpace(request.ChannelPrefix)
            ? "SEED_"
            : request.ChannelPrefix.Trim();
        // Idempotency-Key is read into the controller for audit-only
        // logging (Sprint-6 trade-off #2). The variable is intentionally
        // discarded — the real dedup is the UNIQUE channel ref index.
        _ = idempotencyKey;

        // Build a fresh channel ref per call so the UNIQUE index does
        // not block repeated seeds. ULID-shaped suffix mirrors the
        // frontend's idempotency-key shape.
        var now = _clock.GetUtcNow().UtcDateTime;
        var suffix = now.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture)
            + "-"
            + Guid.NewGuid().ToString("N")[..8];
        var channelRef = $"{prefix}{suffix}";

        var lines = Enumerable
            .Range(1, lineCount)
            .Select(i => ($"SEED-SKU-{i}", 1, (int?)100))
            .ToArray();

        var orderResult = Order.Create(
            channelExternalOrderId: channelRef,
            shippingProfile: "standard",
            lines: lines
        );
        if (!orderResult.IsSuccess)
        {
            return ProblemFromResult(orderResult.Error!, orderResult.ErrorCode!);
        }

        var order = orderResult.Value!;
        await _orderRepo.AddAsync(order, ct).ConfigureAwait(false);

        var placedPayload = new OrderPlacedV1(
            OrderId: order.Id,
            TenantId: _requestContext.TenantId,
            ChannelExternalOrderId: order.ChannelExternalOrderId,
            ShippingProfile: order.ShippingProfile,
            Lines: order
                .Lines.Select(l => new OrderPlacedLineV1(
                    OrderLineId: l.Id.ToString(),
                    Sku: l.Sku,
                    Qty: l.Qty,
                    ExpectedWeight: l.ExpectedWeight
                ))
                .ToArray(),
            OccurredAt: now
        );
        await _outbox
            .AppendAsync(OrderPlacedV1EventType, placedPayload, ct)
            .ConfigureAwait(false);

        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);

        return CreatedAtAction(nameof(GetByIdAsync), new { id = order.Id }, Map(order));
    }

    /// <summary>
    /// U6 — picker reports the order is picked. Order moves
    /// AwaitingPick → Picked; saga receives <see cref="PickConfirmed"/>
    /// (in-process publish via <see cref="IPublishEndpoint"/>) and
    /// transitions to its own Picked state.
    /// </summary>
    [HttpPost("{id:guid}/confirm-pick")]
    public async Task<IActionResult> ConfirmPickAsync(Guid id, CancellationToken ct)
    {
        var order = await _orderRepo.FindByIdAsync(id, ct).ConfigureAwait(false);
        if (order is null)
        {
            return ProblemFromError($"order {id} not found.", "order.not_found", 404);
        }

        var transition = order.MarkPicked();
        if (!transition.IsSuccess)
        {
            return ProblemFromResult(transition.Error!, transition.ErrorCode!);
        }

        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);

        // Publish the in-process saga event AFTER the order commit lands —
        // MassTransit's saga middleware will pick it up on the next dispatch
        // tick and commit the saga transition in its own EF transaction.
        await _publishEndpoint.Publish(new PickConfirmed(order.Id), ct).ConfigureAwait(false);

        return Ok(Map(order));
    }

    /// <summary>
    /// U7 — operator reports the order cannot be picked (typically a
    /// physical stock discrepancy discovered at the bin). Order moves
    /// AwaitingPick → CompensatingReservation; the saga's
    /// <see cref="PickFailed"/> handler transitions to its own
    /// CompensatingReservation state and publishes
    /// <c>ReleaseStockV1</c>. When all expected <c>StockReleasedV1</c>
    /// events arrive (Set-based dedup against MT redelivery), the saga
    /// transitions to Cancelled + publishes <c>OrderCancelled</c> for the
    /// Outbound-side consumer to flip the Order row to <c>Cancelled</c>
    /// (R3 eventual-consistency boundary).
    /// </summary>
    [HttpPost("{id:guid}/mark-pick-failed")]
    public async Task<IActionResult> MarkPickFailedAsync(
        Guid id,
        [FromBody] MarkPickFailedRequest? request,
        CancellationToken ct
    )
    {
        var order = await _orderRepo.FindByIdAsync(id, ct).ConfigureAwait(false);
        if (order is null)
        {
            return ProblemFromError($"order {id} not found.", "order.not_found", 404);
        }

        // Defensive pre-state guard: the saga only handles PickFailed in
        // its AwaitingPick state. If the Order row's status is anything
        // other than AwaitingPick (or the already-compensating state, in
        // which case it's a duplicate POST per the plan's "race" scenario),
        // surface 400 invalid_state. A duplicate POST against a Compensating
        // / Cancelled order returns 409 conflict so operators see the
        // serialization, not a silent no-op.
        if (order.Status == OrderStatus.CompensatingReservation
            || order.Status == OrderStatus.Cancelled)
        {
            return ProblemFromError(
                $"order {id} is already in {order.Status} state; pick-failure already recorded.",
                "order.pick_failure_already_recorded",
                409
            );
        }

        var transition = order.MarkCompensatingReservation();
        if (!transition.IsSuccess)
        {
            return ProblemFromResult(transition.Error!, transition.ErrorCode!);
        }

        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);

        // R3 boundary: publish the in-process saga event AFTER the Order
        // commit. The saga's AwaitingPick → CompensatingReservation
        // transition + the ReleaseStockV1 publish live in a separate MT
        // saga commit. The Reason string is captured on the event for
        // diagnostic logging; not persisted on the Order row (no
        // pick_failed_reason column in the U1 schema — Phase-2 candidate).
        var reason = string.IsNullOrWhiteSpace(request?.Reason) ? string.Empty : request!.Reason!.Trim();
        await _publishEndpoint
            .Publish(new PickFailed(order.Id, reason), ct)
            .ConfigureAwait(false);

        return Ok(Map(order));
    }

    /// <summary>
    /// U6 — packer reports the actual packed weight. Weight-variance
    /// check vs. the expected weight: if &gt; 10% the response carries
    /// <c>weight_warning=true</c> with the signed variance percentage,
    /// but the transition still completes (warning is informational,
    /// not a reject). Order moves Picked → Packed → AwaitingShip in the
    /// same SaveChanges; saga receives <see cref="PackConfirmed"/> and
    /// transitions through its own Packed state on the next dispatch tick.
    /// </summary>
    [HttpPost("{id:guid}/confirm-pack")]
    public async Task<IActionResult> ConfirmPackAsync(
        Guid id,
        [FromBody] ConfirmPackRequest request,
        CancellationToken ct
    )
    {
        if (request is null)
        {
            return ProblemFromError("request body is required.", "order.request_required", 400);
        }
        if (request.ActualWeightTotal < 0)
        {
            return ProblemFromError(
                "actual_weight_total must be non-negative.",
                "order.actual_weight_negative",
                400
            );
        }

        var order = await _orderRepo.FindByIdAsync(id, ct).ConfigureAwait(false);
        if (order is null)
        {
            return ProblemFromError($"order {id} not found.", "order.not_found", 404);
        }

        // MarkPacked requires Picked pre-state per Order's state machine
        // (U2's MarkPacked_FromAwaitingPack_FailsInvalidState locks this).
        // The plan's "Picked OR AwaitingPack" wording acknowledged the U4
        // deviation but Order's state machine keeps Picked-only — see
        // deviations in U6 sign-off.
        var packTransition = order.MarkPacked(request.ActualWeightTotal);
        if (!packTransition.IsSuccess)
        {
            return ProblemFromResult(packTransition.Error!, packTransition.ErrorCode!);
        }

        // Chain Packed → AwaitingShip so the confirm-ship endpoint can
        // call MarkShipped without an explicit intermediate POST. The
        // saga model in the plan declares Packed → AwaitingShip as an
        // auto transition; the Order aggregate runs one step ahead of
        // the saga's view of state, which is fine because the saga is
        // the authoritative state column for cross-module commands and
        // the Order row is the operator-facing state.
        var awaitingShipTransition = order.MarkAwaitingShip();
        if (!awaitingShipTransition.IsSuccess)
        {
            // Should be impossible given we just transitioned to Packed;
            // surface as 500-equivalent so the operator sees the invariant
            // breach rather than the silent rollback.
            return ProblemFromResult(
                awaitingShipTransition.Error!,
                awaitingShipTransition.ErrorCode!
            );
        }

        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);

        // R3 boundary: publish the in-process saga event after the order
        // commit. Saga middleware drives the saga state forward in its
        // own EF transaction.
        await _publishEndpoint
            .Publish(new PackConfirmed(order.Id, request.ActualWeightTotal), ct)
            .ConfigureAwait(false);

        var (warning, variancePct) = ComputeWeightWarning(
            order.ExpectedWeightTotal,
            request.ActualWeightTotal
        );
        return Ok(
            new ConfirmPackResponse(
                Order: Map(order),
                WeightWarning: warning,
                WeightVariancePct: variancePct
            )
        );
    }

    /// <summary>
    /// U6 — final ship confirmation. Calls the mocked carrier (Polly
    /// pipeline handles retries); on success persists label + tracking
    /// number, enqueues <c>ConfirmStockV1</c> + <c>TrackingPushedV1</c>
    /// in the outbox (same SaveChanges), and publishes the in-process
    /// <see cref="ShipConfirmed"/> saga event. On carrier exhaustion
    /// (Polly retries exhausted) returns 503 ProblemDetails
    /// <c>shipping.carrier_unavailable</c>; the order stays in
    /// AwaitingShip; no Inventory commands published.
    /// </summary>
    [HttpPost("{id:guid}/confirm-ship")]
    public async Task<IActionResult> ConfirmShipAsync(Guid id, CancellationToken ct)
    {
        var order = await _orderRepo.FindByIdAsync(id, ct).ConfigureAwait(false);
        if (order is null)
        {
            return ProblemFromError($"order {id} not found.", "order.not_found", 404);
        }

        if (order.Status != OrderStatus.AwaitingShip)
        {
            return ProblemFromError(
                $"cannot ship order in {order.Status} state; required pre-state AwaitingShip.",
                "order.invalid_state",
                400
            );
        }

        ShippingLabel label;
        try
        {
            label = await _shippingProvider
                .CreateLabelAsync(order, ct)
                .ConfigureAwait(false);
        }
        catch (TransientShippingException ex)
        {
            // Polly retries exhausted. Order stays in AwaitingShip — no
            // state change persisted, no outbox rows enqueued. Operator
            // can retry the endpoint; the Polly pipeline will spin up
            // again. The 503 ProblemDetails carries no exception detail
            // (operator doesn't need it).
            _ = ex;
            return ProblemFromError(
                "shipping carrier unavailable after retries.",
                "shipping.carrier_unavailable",
                503
            );
        }

        var shipTransition = order.MarkShipped(label.LabelUrl, label.TrackingNumber);
        if (!shipTransition.IsSuccess)
        {
            // Defensive: should be impossible given the AwaitingShip check
            // above, but if the Domain rejects (e.g. blank label_url) we
            // surface rather than swallow.
            return ProblemFromResult(shipTransition.Error!, shipTransition.ErrorCode!);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var confirmPayload = new ConfirmStockV1(
            OrderId: order.Id,
            TenantId: _requestContext.TenantId
        );
        var trackingPayload = new TrackingPushedV1(
            OrderId: order.Id,
            TenantId: _requestContext.TenantId,
            TrackingNumber: label.TrackingNumber,
            LabelUrl: label.LabelUrl,
            ChannelId: null,
            OccurredAt: now
        );
        await _outbox
            .AppendAsync(ConfirmStockV1EventType, confirmPayload, ct)
            .ConfigureAwait(false);
        await _outbox
            .AppendAsync(TrackingPushedV1EventType, trackingPayload, ct)
            .ConfigureAwait(false);

        // Single SaveChanges commits the order update + both outbox rows
        // in one EF transaction. The saga commit is separate.
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);

        await _publishEndpoint
            .Publish(
                new ShipConfirmed(order.Id, label.LabelUrl, label.TrackingNumber),
                ct
            )
            .ConfigureAwait(false);

        return Ok(
            new ConfirmShipResponse(
                LabelUrl: label.LabelUrl,
                TrackingNumber: label.TrackingNumber,
                Order: Map(order)
            )
        );
    }

    private static (bool Warning, double? VariancePct) ComputeWeightWarning(
        int? expectedWeightTotal,
        int actualWeightTotal
    )
    {
        if (!expectedWeightTotal.HasValue || expectedWeightTotal.Value == 0)
        {
            return (false, null);
        }
        var signedDelta = (double)(actualWeightTotal - expectedWeightTotal.Value);
        var variancePct = signedDelta / expectedWeightTotal.Value * 100.0;
        var warning = Math.Abs(variancePct) > WeightWarningThreshold * 100.0;
        return (warning, variancePct);
    }

    private IActionResult ProblemFromError(string detail, string code, int status) =>
        Problem(
            statusCode: status,
            title: detail,
            type: $"https://shopflow.example/errors/{code}"
        );

    private IActionResult ProblemFromResult(string detail, string code)
    {
        var status = code.EndsWith("not_found", StringComparison.Ordinal) ? 404 : 400;
        return ProblemFromError(detail, code, status);
    }

    private static OrderResponse Map(Order order) =>
        new(
            Id: order.Id,
            ChannelExternalOrderId: order.ChannelExternalOrderId,
            ShippingProfile: order.ShippingProfile,
            Status: order.Status.ToString(),
            ExpectedWeightTotal: order.ExpectedWeightTotal,
            ActualWeightTotal: order.ActualWeightTotal,
            LabelUrl: order.LabelUrl,
            TrackingNumber: order.TrackingNumber,
            PickWaveId: order.PickWaveId,
            Lines: order
                .Lines.Select(l => new OrderLineResponse(l.Id, l.Sku, l.Qty, l.ExpectedWeight))
                .ToArray()
        );
}
