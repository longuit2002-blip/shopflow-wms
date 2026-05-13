using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShopFlow.Outbound.Application;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Domain;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;

namespace ShopFlow.Outbound.Infrastructure.Workers;

/// <summary>
/// Sprint-3-redux U5 K4 — multiplexed pick-wave generator. A single
/// instance ticks on a 30-second <see cref="PeriodicTimer"/> and, on
/// each tick:
/// <list type="number">
///   <item><description>Enumerates every <see cref="TenantStatus.Ready"/> tenant via <see cref="ITenantCatalog"/>.</description></item>
///   <item><description>For each tenant, drains the <see cref="IPickQueue"/> reader via <c>TryRead</c> until empty + accumulates items into per-<c>(tenant, shipping_profile)</c> in-memory buffers.</description></item>
///   <item><description>For each buffer whose oldest item has aged past 15 min OR whose count has reached <see cref="MaxWaveSize"/>=50, opens a per-tenant scope (binds <see cref="RequestContext"/>), assigns a picker via the per-tenant round-robin cursor, materialises a <see cref="PickWave"/> + N <see cref="PickAssignment"/> rows, attaches each order to the wave via <see cref="Order.AttachToPickWave"/>, and commits via <see cref="IUnitOfWork.SaveChangesAsync"/>.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>Pattern mirrors Sprint-1-redux's
/// <see cref="ShopFlow.Inventory.Infrastructure.Workers"/>'
/// <c>ReservationExpiryWorker</c>: <see cref="PeriodicTimer"/> with
/// the injected <see cref="TimeProvider"/> so tests can advance the
/// clock; per-tick fresh DI scope; per-tenant try/catch isolation so
/// one tenant's failure doesn't block the others.</para>
///
/// <para>Single-instance assumption: Phase-1's Aspire AppHost runs one
/// Outbound.Api process, so the in-memory buffers + round-robin
/// cursors hold authoritative state. Phase-2's multi-instance leader
/// election + persistent buffer recovery is tracked in the plan's risk
/// row.</para>
///
/// <para>Round-robin: per-tenant integer cursor monotonically
/// increments per wave emit; modulo picker-count picks the next picker
/// each time. Deterministic for tests (no <see cref="Random"/> seed
/// here, consistent with AGENTS.md §5.36 — Random.Shared would be the
/// fallback if randomisation were needed).</para>
///
/// <para>Wave size is capped at <see cref="MaxWaveSize"/>. The window
/// also closes by time when the oldest item in the buffer has aged
/// past <see cref="WindowAge"/> (15 min) per plan AE4.</para>
/// </remarks>
public sealed class PickWaveGeneratorService : BackgroundService
{
    /// <summary>Tick interval — 30 s per K4.</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    /// <summary>Sliding-window age trigger — 15 min per R10.</summary>
    public static readonly TimeSpan WindowAge = TimeSpan.FromMinutes(15);

    /// <summary>Per-wave size cap — 50 orders per R10 / AE4.</summary>
    public const int MaxWaveSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPickQueue _pickQueue;
    private readonly TimeProvider _clock;
    private readonly ILogger<PickWaveGeneratorService> _logger;

    // Per-(tenant, profile) in-memory buffer. Items survive across ticks
    // until the buffer closes via age-or-size trigger. Not persistent —
    // a host restart loses unflushed items (rare in Phase-1; Phase-2
    // leader-election handover will need a persistent buffer recovery
    // path, tracked in the plan's risk row).
    private readonly Dictionary<(Guid TenantId, string Profile), List<PickRequestV1>> _buffers =
        new();

    // Per-tenant round-robin cursor; incremented each time a wave for
    // that tenant emits. Modulo picker-count selects the next picker.
    private readonly Dictionary<Guid, int> _pickerCursor = new();

    public PickWaveGeneratorService(
        IServiceScopeFactory scopeFactory,
        IPickQueue pickQueue,
        TimeProvider clock,
        ILogger<PickWaveGeneratorService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(pickQueue);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _pickQueue = pickQueue;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "PickWaveGeneratorService started; tickInterval={Tick}, windowAge={Window}, maxWaveSize={Max}",
            (int)TickInterval.TotalSeconds,
            (int)WindowAge.TotalMinutes,
            MaxWaveSize
        );

        using var timer = new PeriodicTimer(TickInterval, _clock);
        try
        {
            await TickAsync(stoppingToken).ConfigureAwait(false);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
        finally
        {
            _logger.LogInformation("PickWaveGeneratorService stopping.");
        }
    }

    /// <summary>
    /// Entry point exposed for U5 unit tests + future single-shot test
    /// hooks. Each call performs one tick of the multiplexed drain +
    /// emit cycle. <see cref="ExecuteAsync"/> drives this on a 30-second
    /// <see cref="PeriodicTimer"/>; tests invoke it directly so they
    /// don't pay the timer's wall-clock latency.
    /// </summary>
    public async Task TickAsync(CancellationToken ct)
    {
        await using var rootScope = _scopeFactory.CreateAsyncScope();
        IReadOnlyList<TenantInfo> tenants;
        try
        {
            var catalog = rootScope.ServiceProvider.GetRequiredService<ITenantCatalog>();
            tenants = await catalog.GetReadyTenantsAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "PickWaveGeneratorService failed to enumerate tenants this tick.");
            return;
        }

        if (tenants.Count == 0)
        {
            return;
        }

        foreach (var tenant in tenants)
        {
            try
            {
                await ProcessTenantAsync(tenant, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "PickWaveGeneratorService tenant {TenantSlug} tick failed; other tenants continue.",
                    tenant.Slug
                );
            }
        }
    }

    private async Task ProcessTenantAsync(TenantInfo tenant, CancellationToken ct)
    {
        // Step 1 — drain this tenant's channel into per-profile buffers.
        var reader = _pickQueue.GetReader(tenant.Id);
        while (reader.TryRead(out var item))
        {
            var key = (tenant.Id, item.ShippingProfile);
            if (!_buffers.TryGetValue(key, out var bucket))
            {
                bucket = new List<PickRequestV1>();
                _buffers[key] = bucket;
            }
            bucket.Add(item);
        }

        // Step 2 — identify buffers ready to flush (age OR size trigger).
        var now = _clock.GetUtcNow().UtcDateTime;
        var keysToFlush = _buffers
            .Where(kv => kv.Key.TenantId == tenant.Id && kv.Value.Count > 0)
            .Where(kv =>
                kv.Value.Count >= MaxWaveSize
                || (now - kv.Value[0].EnqueuedAt) >= WindowAge
            )
            .Select(kv => kv.Key)
            .ToList();

        if (keysToFlush.Count == 0)
        {
            return;
        }

        // Step 3 — open a per-tenant scope, bind RequestContext BEFORE
        // resolving the OutboundDbContext (which reads
        // IRequestContext.DbConnectionString at construction).
        await using var tenantScope = _scopeFactory.CreateAsyncScope();
        var rc = tenantScope.ServiceProvider.GetRequiredService<RequestContext>();
        rc.Bind(tenant, Guid.NewGuid().ToString("N"), userId: null);

        var pickWaveRepo = tenantScope.ServiceProvider.GetRequiredService<IPickWaveRepository>();
        var pickerRepo = tenantScope.ServiceProvider.GetRequiredService<IPickerRepository>();
        var orderRepo = tenantScope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var uow = tenantScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var pickers = await pickerRepo.ListByTenantAsync(ct).ConfigureAwait(false);
        if (pickers.Count == 0)
        {
            _logger.LogInformation(
                "PickWaveGeneratorService tenant {TenantSlug} has no pickers seeded; skipping wave emit this tick.",
                tenant.Slug
            );
            return;
        }

        foreach (var key in keysToFlush)
        {
            // Pull items + remove the buffer entry — wave is "in flight"
            // now; failure on SaveChanges below loses the buffer's
            // contents for this tick (Phase-2 persistent buffer is the
            // recovery path).
            var items = _buffers[key];
            _buffers.Remove(key);

            if (!_pickerCursor.TryGetValue(tenant.Id, out var cursor))
            {
                cursor = 0;
            }
            var picker = pickers[cursor % pickers.Count];
            _pickerCursor[tenant.Id] = cursor + 1;

            var wave = PickWave.Open(key.Profile, picker.PickerId, now);
            foreach (var item in items)
            {
                wave.AssignOrder(item.OrderId, now);
                var order = await orderRepo
                    .FindByIdAsync(item.OrderId, ct)
                    .ConfigureAwait(false);
                if (order is not null)
                {
                    order.AttachToPickWave(wave.Id);
                }
            }
            wave.Close(now);
            await pickWaveRepo.AddAsync(wave, ct).ConfigureAwait(false);
            await uow.SaveChangesAsync(ct).ConfigureAwait(false);

            _logger.LogInformation(
                "PickWaveGeneratorService emitted wave {WaveId} for tenant {TenantSlug} profile {Profile} picker {Picker} with {Count} orders.",
                wave.Id,
                tenant.Slug,
                key.Profile,
                picker.PickerId,
                items.Count
            );
        }
    }
}
