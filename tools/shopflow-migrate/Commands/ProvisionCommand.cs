using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopFlow.ControlPlane.Infrastructure;
using ShopFlow.Migrate.Provisioning;

namespace ShopFlow.Migrate.Commands;

/// <summary>
/// <c>provision --catalog</c> creates the <c>shopflow_control</c> DB if
/// absent and applies the catalog migrations. <c>provision --tenant=&lt;slug&gt;</c>
/// delegates to <see cref="ITenantProvisioner"/>. Exactly one of the two
/// flags must be set.
/// </summary>
public sealed class ProvisionCommand : ICommand
{
    public const string CatalogFlag = "catalog";
    public const string TenantFlag = "tenant";

    private readonly ITenantProvisioner _provisioner;
    private readonly IPostgresAdmin _admin;
    private readonly ControlPlaneDbContext _catalogDb;
    private readonly ILogger<ProvisionCommand> _logger;

    public ProvisionCommand(
        ITenantProvisioner provisioner,
        IPostgresAdmin admin,
        ControlPlaneDbContext catalogDb,
        ILogger<ProvisionCommand> logger
    )
    {
        _provisioner = provisioner;
        _admin = admin;
        _catalogDb = catalogDb;
        _logger = logger;
    }

    public string Name => ParsedArgs.SubcommandProvision;

    public async Task<int> ExecuteAsync(ParsedArgs args, CancellationToken ct)
    {
        var hasCatalog = args.HasFlag(CatalogFlag);
        var tenantSlug = args.GetFlag(TenantFlag);
        var hasTenant = !string.IsNullOrWhiteSpace(tenantSlug);

        if (hasCatalog == hasTenant)
        {
            Console.Error.WriteLine(
                "provision requires exactly one of '--catalog' or '--tenant=<slug>'."
            );
            return 2;
        }

        if (hasCatalog)
        {
            return await ProvisionCatalogAsync(ct).ConfigureAwait(false);
        }

        var outcome = await _provisioner.ProvisionAsync(tenantSlug!, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Tenant '{Slug}' provisioning complete: {Outcome}.",
            tenantSlug,
            outcome
        );
        return 0;
    }

    private async Task<int> ProvisionCatalogAsync(CancellationToken ct)
    {
        var catalogDbName = _catalogDb.Database.GetDbConnection().Database
            ?? throw new InvalidOperationException(
                "control-plane connection string is missing a database name."
            );

        if (!await _admin.DatabaseExistsAsync(catalogDbName, ct).ConfigureAwait(false))
        {
            await _admin.CreateDatabaseAsync(catalogDbName, ct).ConfigureAwait(false);
        }
        else
        {
            _logger.LogInformation(
                "Catalog database '{DbName}' already exists; skipping create.",
                catalogDbName
            );
        }

        await _catalogDb.Database.MigrateAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Catalog migrations applied to '{DbName}'.", catalogDbName);
        return 0;
    }
}
