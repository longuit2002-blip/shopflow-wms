using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ShopFlow.SharedKernel.Infrastructure;
using ShopFlow.StockSync.Application.Coalescing;
using ShopFlow.StockSync.Application.Options;

namespace ShopFlow.StockSync.Api.Controllers;

/// <summary>
/// Sprint-5 plan U8 — read-only diagnostics surface for the StockSync
/// engine's in-memory state. Returns the singleton
/// <see cref="ICoalescingBuffer"/> entry count plus a snapshot of the
/// effective <see cref="StockSyncOptions"/> binding so operators can verify
/// configuration without re-deploying.
/// </summary>
/// <remarks>
/// <para>Gated by <see cref="StockSyncOptions.DiagnosticsEnabled"/>: when
/// the flag is <c>false</c> (production default), the endpoint returns 404
/// so the surface is invisible to scanners. Development overrides via
/// <c>appsettings.Development.json</c>. Phase-3 replaces the bare flag with
/// proper admin-API auth.</para>
///
/// <para><see cref="SkipTenantRoutingAttribute"/> bypasses
/// <see cref="TenantRoutingMiddleware"/> because the diagnostics view is
/// process-level — there's no tenant DB query, only in-memory state. Without
/// the attribute the middleware's default-deny posture would 400 every
/// header-less call.</para>
/// </remarks>
[ApiController]
[Route("api/sync")]
[SkipTenantRouting]
public sealed class SyncStateController : ControllerBase
{
    private readonly ICoalescingBuffer _buffer;
    private readonly IOptions<StockSyncOptions> _options;

    public SyncStateController(ICoalescingBuffer buffer, IOptions<StockSyncOptions> options)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(options);
        _buffer = buffer;
        _options = options;
    }

    [HttpGet("state")]
    public IActionResult GetState()
    {
        var opts = _options.Value;
        if (!opts.DiagnosticsEnabled)
        {
            return NotFound();
        }

        return Ok(
            new
            {
                buffer = new { count = _buffer.Count },
                options = new
                {
                    coalesceWindowMs = opts.CoalesceWindowMs,
                    activeChannels = opts.ActiveChannels,
                    tokenBucket = new
                    {
                        sustain = opts.TokenBucket.Sustain,
                        burst = opts.TokenBucket.Burst,
                        queueLimit = opts.TokenBucket.QueueLimit,
                    },
                    queueCapacity = new
                    {
                        highCap = opts.QueueCapacity.HighCap,
                        normalCap = opts.QueueCapacity.NormalCap,
                    },
                    breaker = new
                    {
                        minimumThroughput = opts.Breaker.MinimumThroughput,
                        breakDurationSeconds = opts.Breaker.BreakDurationSeconds,
                        samplingDurationSeconds = opts.Breaker.SamplingDurationSeconds,
                    },
                },
                timestamp = DateTime.UtcNow,
            }
        );
    }
}
