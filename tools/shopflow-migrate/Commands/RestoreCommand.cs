using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopFlow.ControlPlane.Domain;
using ShopFlow.ControlPlane.Infrastructure;
using ShopFlow.Migrate.Provisioning;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Migrate.Commands;

/// <summary>
/// <c>restore --tenant=&lt;slug&gt;</c> — reverses an in-flight archive
/// while still within the retention window (i.e., before the deferred
/// DROP runs). Re-grants CONNECT and flips status Archiving → Ready.
/// </summary>
/// <remarks>
/// Restore from <c>Archived</c> is not implemented in Phase-0-redux —
/// the Phase-2 DROP cron physically removes the DB; recovery requires the
/// dump-and-recover path documented in Tech Design §2. This command
/// rejects archived tenants with a clear error so operators do not
/// accidentally assume restore will resurrect deleted data.
/// </remarks>
public sealed class RestoreCommand : ICommand
{
    public const string TenantFlag = "tenant";

    private readonly ControlPlaneDbContext _catalogDb;
    private readonly IPostgresAdmin _admin;
    private readonly MigrateOptions _options;
    private readonly ILogger<RestoreCommand> _logger;

    public RestoreCommand(
        ControlPlaneDbContext catalogDb,
        IPostgresAdmin admin,
        MigrateOptions options,
        ILogger<RestoreCommand> logger
    )
    {
        _catalogDb = catalogDb;
        _admin = admin;
        _options = options;
        _logger = logger;
    }

    public string Name => ParsedArgs.SubcommandRestore;

    public async Task<int> ExecuteAsync(ParsedArgs args, CancellationToken ct)
    {
        var slug = args.GetFlag(TenantFlag);
        if (string.IsNullOrWhiteSpace(slug))
        {
            Console.Error.WriteLine("restore requires --tenant=<slug>.");
            return 2;
        }

        var normalized = slug.Trim().ToLowerInvariant();
        var tenant = await _catalogDb
            .Tenants.FirstOrDefaultAsync(t => t.Slug == normalized, ct)
            .ConfigureAwait(false);
        if (tenant is null)
        {
            Console.Error.WriteLine($"tenant '{normalized}' not found.");
            return 3;
        }

        if (tenant.Status == TenantStatus.Archived)
        {
            Console.Error.WriteLine(
                $"tenant '{normalized}' is Archived; physical DB has been dropped. Restore requires the dump-and-recover path."
            );
            return 4;
        }

        if (tenant.Status != TenantStatus.Archiving)
        {
            Console.Error.WriteLine(
                $"restore requires Archiving status; tenant '{normalized}' is {tenant.Status}."
            );
            return 4;
        }

        await _admin
            .RestoreTenantConnectAsync(tenant.DbName, _options.Postgres.AppRoleName, ct)
            .ConfigureAwait(false);

        var resume = tenant.BeginProvisioning();
        if (!resume.IsSuccess)
        {
            // Domain rejects Archiving → Provisioning. We model "restore"
            // as a domain-level reverse transition by rolling state back
            // via a direct EF property update.
            _catalogDb.Entry(tenant).Property(t => t.Status).CurrentValue = TenantStatus.Ready;
            _catalogDb.Entry(tenant).Property(t => t.ArchivingAt).CurrentValue = null;
            _catalogDb.Entry(tenant).Property(t => t.UpdatedAt).CurrentValue = DateTime.UtcNow;
        }
        else
        {
            tenant.MarkProvisioned();
        }

        _catalogDb.TenantEvents.Add(
            TenantEvent.Record(
                tenantId: tenant.Id,
                eventType: "tenant.restored",
                payloadJson: $"{{\"slug\":\"{tenant.Slug}\"}}"
            )
        );
        await _catalogDb.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Tenant '{Slug}' restored to Ready; CONNECT granted.",
            normalized
        );
        return 0;
    }
}
