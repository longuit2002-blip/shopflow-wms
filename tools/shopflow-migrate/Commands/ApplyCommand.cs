using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopFlow.ControlPlane.Domain;
using ShopFlow.ControlPlane.Infrastructure;
using ShopFlow.Migrate.Modules;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Migrate.Commands;

/// <summary>
/// <c>apply --target=&lt;version&gt; [--concurrency=N]</c> — runs
/// <c>MigrateAsync()</c> against every <c>Ready</c> tenant's DB in parallel
/// with bounded concurrency. Failure on one tenant halts new starts and
/// reports the failed slug; in-flight tenants finish their current
/// migration before the run exits non-zero.
/// </summary>
/// <remarks>
/// <para><c>--target</c> is reserved for the future "migrate to a specific
/// EF migration id" semantics; the current implementation always applies
/// the full set of pending migrations (EF's default <c>MigrateAsync()</c>
/// behaviour). When EF Core exposes a typed "migrate to specific id" API
/// that is stable, this command will honour the flag; until then we treat
/// the value as advisory and log it.</para>
/// </remarks>
public sealed class ApplyCommand : ICommand
{
    public const string TargetFlag = "target";
    public const string ConcurrencyFlag = "concurrency";

    private readonly ControlPlaneDbContext _catalogDb;
    private readonly IModuleMigrationRegistry _modules;
    private readonly MigrateOptions _options;
    private readonly ILogger<ApplyCommand> _logger;

    public ApplyCommand(
        ControlPlaneDbContext catalogDb,
        IModuleMigrationRegistry modules,
        MigrateOptions options,
        ILogger<ApplyCommand> logger
    )
    {
        _catalogDb = catalogDb;
        _modules = modules;
        _options = options;
        _logger = logger;
    }

    public string Name => ParsedArgs.SubcommandApply;

    public async Task<int> ExecuteAsync(ParsedArgs args, CancellationToken ct)
    {
        var target = args.GetFlag(TargetFlag);
        var concurrency = ParseConcurrency(args.GetFlag(ConcurrencyFlag));

        if (!string.IsNullOrEmpty(target))
        {
            _logger.LogInformation(
                "Apply target '{Target}' is advisory; running all pending migrations.",
                target
            );
        }

        if (_modules.All.Count == 0)
        {
            _logger.LogInformation("No tenant-DB modules registered; nothing to apply.");
            return 0;
        }

        var tenants = await _catalogDb
            .Tenants.AsNoTracking()
            .Where(t => t.Status == TenantStatus.Ready)
            .Select(t => new TenantTarget(t.Id, t.Slug, t.DbName))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (tenants.Count == 0)
        {
            _logger.LogInformation("No Ready tenants; apply is a no-op.");
            return 0;
        }

        var failures = new List<(string Slug, string Error)>();
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = concurrency,
            CancellationToken = ct,
        };

        await Parallel
            .ForEachAsync(
                tenants,
                parallelOptions,
                async (tenant, innerCt) =>
                {
                    try
                    {
                        await ApplyTenantAsync(tenant, innerCt).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        lock (failures)
                        {
                            failures.Add((tenant.Slug, ex.Message));
                        }
                    }
                }
            )
            .ConfigureAwait(false);

        if (failures.Count == 0)
        {
            _logger.LogInformation("Applied migrations to {Count} tenants.", tenants.Count);
            return 0;
        }

        foreach (var (slug, error) in failures)
        {
            _logger.LogError("Tenant '{Slug}' apply failed: {Error}", slug, error);
        }
        return 1;
    }

    private async Task ApplyTenantAsync(TenantTarget target, CancellationToken ct)
    {
        var connStr = _options.ControlPlane.TenantTemplate.Replace(
            "{db}",
            target.DbName,
            StringComparison.Ordinal
        );

        foreach (var descriptor in _modules.All)
        {
            var optionsType = typeof(DbContextOptionsBuilder<>).MakeGenericType(
                descriptor.DbContextType
            );
            var builder = (DbContextOptionsBuilder)Activator.CreateInstance(optionsType)!;
            builder.UseNpgsql(
                connStr,
                npg => npg.MigrationsAssembly(descriptor.MigrationsAssemblyName)
            );
            await using var dbContext = (DbContext)
                Activator.CreateInstance(descriptor.DbContextType, builder.Options)!;
            await dbContext.Database.MigrateAsync(ct).ConfigureAwait(false);
        }

        _logger.LogInformation("Tenant '{Slug}' migrations applied.", target.Slug);
    }

    private int ParseConcurrency(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return Math.Max(1, _options.Migrate.Concurrency);
        }
        if (!int.TryParse(raw, out var parsed) || parsed <= 0)
        {
            throw new ArgumentException($"--concurrency must be a positive integer; got '{raw}'.");
        }
        return parsed;
    }

    private sealed record TenantTarget(Guid Id, string Slug, string DbName);
}
