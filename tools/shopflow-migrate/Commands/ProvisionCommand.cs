using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopFlow.ControlPlane.Infrastructure;
using ShopFlow.Migrate.Provisioning;
using ShopFlow.SharedKernel.Infrastructure;

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
    public const string OwnerEmailFlag = "owner-email";
    public const string OwnerPasswordFlag = "owner-password";
    public const string OwnerPasswordFromEnvFlag = "owner-password-from-env";

    private readonly ITenantProvisioner _provisioner;
    private readonly IPostgresAdmin _admin;
    private readonly ControlPlaneDbContext _catalogDb;
    private readonly OwnerSeed _ownerSeed;
    private readonly MigrateOptions _options;
    private readonly ILogger<ProvisionCommand> _logger;

    public ProvisionCommand(
        ITenantProvisioner provisioner,
        IPostgresAdmin admin,
        ControlPlaneDbContext catalogDb,
        OwnerSeed ownerSeed,
        MigrateOptions options,
        ILogger<ProvisionCommand> logger
    )
    {
        _provisioner = provisioner;
        _admin = admin;
        _catalogDb = catalogDb;
        _ownerSeed = ownerSeed;
        _options = options;
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

        // Sprint-8 U10 — pre-flight slug reservation check. Same list
        // the AuthController subdomain resolver rejects against, so a
        // future provisioning of e.g. "admin" or "api" can't land a
        // tenant that subsequently fails to login because the
        // subdomain is dropped.
        if (ReservedSlugs.IsReserved(tenantSlug))
        {
            Console.Error.WriteLine(
                $"Slug '{tenantSlug}' is reserved and cannot be provisioned. "
                + "See SharedKernel.Infrastructure.ReservedSlugs for the full list."
            );
            return 2;
        }

        var outcome = await _provisioner.ProvisionAsync(tenantSlug!, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Tenant '{Slug}' provisioning complete: {Outcome}.",
            tenantSlug,
            outcome
        );

        // Sprint-8 U10 — owner-seed step. Runs only when provisioning
        // actually created a new tenant DB. AlreadyReady → skip
        // (owner already exists from a prior provision call).
        if (outcome == ProvisionOutcome.AlreadyReady)
        {
            return 0;
        }

        var tenantConnString = _options.ControlPlane.TenantTemplate.Replace(
            "{db}",
            _options.Migrate.DbNamePrefix + tenantSlug!.Trim().ToLowerInvariant(),
            StringComparison.Ordinal);
        var ownerEmail = ResolveOwnerEmail(args, tenantSlug!);
        var explicitPwd = ResolveExplicitPassword(args);

        var seedResult = await _ownerSeed
            .SeedAsync(tenantConnString, ownerEmail, explicitPwd, ct)
            .ConfigureAwait(false);

        EchoOwnerSeed(seedResult, explicitPwd is not null);
        return 0;
    }

    internal static string ResolveOwnerEmail(ParsedArgs args, string slug)
    {
        var supplied = args.GetFlag(OwnerEmailFlag);
        return string.IsNullOrWhiteSpace(supplied)
            ? $"owner@{slug.Trim().ToLowerInvariant()}.local"
            : supplied;
    }

    internal static string? ResolveExplicitPassword(ParsedArgs args)
    {
        var explicitPwd = args.GetFlag(OwnerPasswordFlag);
        if (!string.IsNullOrEmpty(explicitPwd))
        {
            return explicitPwd;
        }
        var envVar = args.GetFlag(OwnerPasswordFromEnvFlag);
        if (string.IsNullOrEmpty(envVar))
        {
            return null;
        }
        var value = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"--owner-password-from-env={envVar} is empty/unset. Provide a value or omit the flag.");
        }
        return value;
    }

    internal static void EchoOwnerSeed(OwnerSeedResult seedResult, bool passwordWasExplicit)
    {
        if (seedResult.Outcome == OwnerSeedOutcome.AlreadySeeded)
        {
            Console.Out.WriteLine(
                $"Owner '{seedResult.OwnerEmail}' already exists in tenant DB; seed skipped.");
            return;
        }

        if (passwordWasExplicit || seedResult.GeneratedPassword is null)
        {
            Console.Out.WriteLine(
                $"Created {seedResult.OwnerEmail} — password set from explicit input (not echoed).");
            return;
        }

        Console.Out.WriteLine(
            $"Created {seedResult.OwnerEmail} — temporary password: {seedResult.GeneratedPassword}");
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
