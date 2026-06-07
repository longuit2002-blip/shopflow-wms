using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopFlow.ControlPlane.Domain;
using ShopFlow.ControlPlane.Infrastructure;
using ShopFlow.Migrate.Modules;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Migrate.Provisioning;

/// <summary>
/// Orchestrates the per-tenant provisioning workflow. The Tenant aggregate
/// owns the lifecycle state machine; this class drives the side-effects:
/// CREATE DATABASE, per-module <c>MigrateAsync()</c>, GRANT privileges, and
/// emits a <c>TenantEvent</c> row for audit.
/// </summary>
/// <remarks>
/// <para>Idempotency: the workflow reads the current tenant status and
/// dispatches accordingly — Pending or ProvisioningFailed both lead to the
/// same forward path (the only difference is whether the catalog row
/// already exists). Ready is a no-op; transitions to Archiving/Archived are
/// rejected at the domain layer.</para>
/// <para>The audit row is written in the same transaction as the
/// <c>MarkProvisioned()</c> state flip so an observer can tail
/// <c>tenant_events</c> for accurate provisioning timing.</para>
/// </remarks>
public sealed class TenantProvisioner : ITenantProvisioner
{
    private readonly ControlPlaneDbContext _catalogDb;
    private readonly IPostgresAdmin _admin;
    private readonly IModuleMigrationRegistry _modules;
    private readonly MigrateOptions _options;
    private readonly ILogger<TenantProvisioner> _logger;

    public TenantProvisioner(
        ControlPlaneDbContext catalogDb,
        IPostgresAdmin admin,
        IModuleMigrationRegistry modules,
        MigrateOptions options,
        ILogger<TenantProvisioner> logger
    )
    {
        _catalogDb = catalogDb;
        _admin = admin;
        _modules = modules;
        _options = options;
        _logger = logger;
    }

    public async Task<ProvisionOutcome> ProvisionAsync(string slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("slug is required.", nameof(slug));
        }

        var normalized = slug.Trim().ToLowerInvariant();
        var dbName = _options.Migrate.DbNamePrefix + normalized;

        var tenant = await _catalogDb
            .Tenants.FirstOrDefaultAsync(t => t.Slug == normalized, ct)
            .ConfigureAwait(false);

        var isNew = false;
        if (tenant is null)
        {
            var create = Tenant.Create(
                slug: normalized,
                dbName: dbName,
                region: _options.ControlPlane.DefaultRegion,
                tier: _options.ControlPlane.DefaultTier
            );
            if (!create.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"failed to create tenant aggregate for '{normalized}': {create.Error}"
                );
            }

            tenant = create.Value!;
            _catalogDb.Tenants.Add(tenant);
            await _catalogDb.SaveChangesAsync(ct).ConfigureAwait(false);
            isNew = true;
            _logger.LogInformation("Registered new tenant '{Slug}' in catalog.", normalized);
        }

        if (tenant.Status == TenantStatus.Ready)
        {
            _logger.LogInformation(
                "Tenant '{Slug}' is already Ready; provision is a no-op.",
                normalized
            );
            return ProvisionOutcome.AlreadyReady;
        }

        if (tenant.Status is TenantStatus.Archiving or TenantStatus.Archived)
        {
            throw new InvalidOperationException(
                $"cannot provision tenant '{normalized}' from status '{tenant.Status}'. Use 'restore' first."
            );
        }

        var begin = tenant.BeginProvisioning();
        if (!begin.IsSuccess)
        {
            throw new InvalidOperationException(
                $"failed to begin provisioning for '{normalized}': {begin.Error}"
            );
        }
        await _catalogDb.SaveChangesAsync(ct).ConfigureAwait(false);

        try
        {
            await EnsureAppRoleAsync(ct).ConfigureAwait(false);
            await EnsureDatabaseAsync(tenant.DbName, ct).ConfigureAwait(false);
            await ApplyModuleMigrationsAsync(tenant.DbName, ct).ConfigureAwait(false);
            await _admin
                .GrantTenantPrivilegesAsync(tenant.DbName, _options.Postgres.AppRoleName, ct)
                .ConfigureAwait(false);

            var done = tenant.MarkProvisioned();
            if (!done.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"failed to mark provisioned for '{normalized}': {done.Error}"
                );
            }

            _catalogDb.TenantEvents.Add(
                TenantEvent.Record(
                    tenantId: tenant.Id,
                    eventType: "tenant.provisioned",
                    payloadJson: $"{{\"slug\":\"{tenant.Slug}\",\"db_name\":\"{tenant.DbName}\"}}"
                )
            );
            await _catalogDb.SaveChangesAsync(ct).ConfigureAwait(false);

            _logger.LogInformation("Provisioned tenant '{Slug}' → Ready.", normalized);
            return isNew ? ProvisionOutcome.Provisioned : ProvisionOutcome.Resumed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var fail = tenant.MarkProvisioningFailed(ex.Message);
            if (fail.IsSuccess)
            {
                await _catalogDb.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }
            throw;
        }
    }

    private async Task EnsureAppRoleAsync(CancellationToken ct)
    {
        await _admin
            .EnsureLoginRoleAsync(
                _options.Postgres.AppRoleName,
                _options.Postgres.AppRolePassword,
                ct
            )
            .ConfigureAwait(false);
    }

    private async Task EnsureDatabaseAsync(string dbName, CancellationToken ct)
    {
        if (await _admin.DatabaseExistsAsync(dbName, ct).ConfigureAwait(false))
        {
            _logger.LogInformation("Database {DbName} already exists; skipping create.", dbName);
            return;
        }
        await _admin.CreateDatabaseAsync(dbName, ct).ConfigureAwait(false);
    }

    private async Task ApplyModuleMigrationsAsync(string dbName, CancellationToken ct)
    {
        if (_modules.All.Count == 0)
        {
            _logger.LogInformation(
                "No tenant-DB modules registered; tenant '{DbName}' provisioned without business schema.",
                dbName
            );
            return;
        }

        var template = _options.ControlPlane.TenantTemplate;
        var tenantConnString = template.Replace("{db}", dbName, StringComparison.Ordinal);

        foreach (var descriptor in _modules.All)
        {
            await ApplyOneModuleAsync(descriptor, tenantConnString, ct).ConfigureAwait(false);
        }
    }

    private async Task ApplyOneModuleAsync(
        ModuleMigrationDescriptor descriptor,
        string connectionString,
        CancellationToken ct
    )
    {
        _logger.LogInformation(
            "Applying migrations: module={Module} dbcontext={Context}",
            descriptor.ModuleName,
            descriptor.DbContextType.Name
        );

        var optionsType = typeof(DbContextOptionsBuilder<>).MakeGenericType(
            descriptor.DbContextType
        );
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(optionsType)!;
        builder.UseNpgsql(
            connectionString,
            npg => npg.MigrationsAssembly(descriptor.MigrationsAssemblyName)
        );

        await using var dbContext = (DbContext)
            Activator.CreateInstance(descriptor.DbContextType, builder.Options)!;
        await dbContext.Database.MigrateAsync(ct).ConfigureAwait(false);
    }
}
