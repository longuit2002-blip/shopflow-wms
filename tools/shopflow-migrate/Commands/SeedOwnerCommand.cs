using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.Migrate.Provisioning;

namespace ShopFlow.Migrate.Commands;

/// <summary>
/// Sprint-8 U10b — <c>seed-owner --tenant=&lt;slug&gt;</c> subcommand.
/// Retrofits one Owner row into a pre-existing tenant DB whose
/// AddUsers migration applied via the standard <c>apply</c> path but
/// whose <c>users</c> table is empty. Operators MUST run this against
/// every legacy tenant before deploying Sprint-8 to prod — otherwise
/// the dev-mode baked JWT removal locks them out (ADV-003 mitigation).
/// </summary>
/// <remarks>
/// <para>Same flag set as <c>provision</c>'s owner-seed step:
/// <c>--owner-email</c> / <c>--owner-password</c> /
/// <c>--owner-password-from-env</c>. Skips the database-create + the
/// catalog state-machine work — pure seed only against the existing
/// tenant DB the catalog already points at.</para>
/// </remarks>
public sealed class SeedOwnerCommand : ICommand
{
    public const string TenantFlag = "tenant";
    public const string SubcommandName = "seed-owner";

    private readonly ITenantCatalog _tenantCatalog;
    private readonly OwnerSeed _ownerSeed;
    private readonly ILogger<SeedOwnerCommand> _logger;

    public SeedOwnerCommand(
        ITenantCatalog tenantCatalog,
        OwnerSeed ownerSeed,
        ILogger<SeedOwnerCommand> logger)
    {
        _tenantCatalog = tenantCatalog;
        _ownerSeed = ownerSeed;
        _logger = logger;
    }

    public string Name => SubcommandName;

    public async Task<int> ExecuteAsync(ParsedArgs args, CancellationToken ct)
    {
        var slug = args.GetFlag(TenantFlag);
        if (string.IsNullOrWhiteSpace(slug))
        {
            Console.Error.WriteLine("seed-owner requires '--tenant=<slug>'.");
            return 2;
        }

        var normalized = slug.Trim().ToLowerInvariant();
        var tenant = await _tenantCatalog
            .LookupBySlugAsync(normalized, ct)
            .ConfigureAwait(false);
        if (tenant is null)
        {
            Console.Error.WriteLine(
                $"Tenant '{normalized}' not found in catalog. Provision it first via 'provision --tenant={normalized}'.");
            return 2;
        }

        var explicitPwd = ProvisionCommand.ResolveExplicitPassword(args);
        var ownerEmail = ProvisionCommand.ResolveOwnerEmail(args, normalized);

        var seedResult = await _ownerSeed
            .SeedAsync(tenant.DbConnectionString, ownerEmail, explicitPwd, ct)
            .ConfigureAwait(false);

        ProvisionCommand.EchoOwnerSeed(seedResult, explicitPwd is not null);
        return 0;
    }
}
