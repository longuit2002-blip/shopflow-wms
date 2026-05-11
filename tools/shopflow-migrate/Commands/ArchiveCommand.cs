using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopFlow.ControlPlane.Domain;
using ShopFlow.ControlPlane.Infrastructure;
using ShopFlow.Migrate.Provisioning;

namespace ShopFlow.Migrate.Commands;

/// <summary>
/// <c>archive --tenant=&lt;slug&gt;</c> — flip the catalog status
/// Ready → Archiving, revoke CONNECT for the app role, and terminate
/// live sessions. Per plan U6, the deferred <c>DROP DATABASE</c> is a
/// Phase-2 cron job triggered after the retention window; this command
/// only does the synchronous part.
/// </summary>
public sealed class ArchiveCommand : ICommand
{
    public const string TenantFlag = "tenant";

    private readonly ControlPlaneDbContext _catalogDb;
    private readonly IPostgresAdmin _admin;
    private readonly MigrateOptions _options;
    private readonly ILogger<ArchiveCommand> _logger;

    public ArchiveCommand(
        ControlPlaneDbContext catalogDb,
        IPostgresAdmin admin,
        MigrateOptions options,
        ILogger<ArchiveCommand> logger
    )
    {
        _catalogDb = catalogDb;
        _admin = admin;
        _options = options;
        _logger = logger;
    }

    public string Name => ParsedArgs.SubcommandArchive;

    public async Task<int> ExecuteAsync(ParsedArgs args, CancellationToken ct)
    {
        var slug = args.GetFlag(TenantFlag);
        if (string.IsNullOrWhiteSpace(slug))
        {
            Console.Error.WriteLine("archive requires --tenant=<slug>.");
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

        var begin = tenant.BeginArchiving();
        if (!begin.IsSuccess)
        {
            Console.Error.WriteLine(
                $"archive rejected: {begin.Error} (current status: {tenant.Status})"
            );
            return 4;
        }

        await _admin
            .RevokeTenantConnectAsync(tenant.DbName, _options.Postgres.AppRoleName, ct)
            .ConfigureAwait(false);

        _catalogDb.TenantEvents.Add(
            TenantEvent.Record(
                tenantId: tenant.Id,
                eventType: "tenant.archiving",
                payloadJson: $"{{\"slug\":\"{tenant.Slug}\",\"db_name\":\"{tenant.DbName}\"}}"
            )
        );
        await _catalogDb.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Tenant '{Slug}' moved to Archiving; CONNECT revoked. DROP deferred per plan U6.",
            normalized
        );
        return 0;
    }
}
