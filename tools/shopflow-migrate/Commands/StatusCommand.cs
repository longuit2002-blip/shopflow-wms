using Microsoft.EntityFrameworkCore;
using ShopFlow.ControlPlane.Infrastructure;

namespace ShopFlow.Migrate.Commands;

/// <summary>
/// <c>status</c> — prints one row per tenant with id, slug, status,
/// db_name, provisioned_at. Used by operators and the Aspire startup hook
/// to confirm dev tenants are Ready before app services start.
/// </summary>
public sealed class StatusCommand : ICommand
{
    private readonly ControlPlaneDbContext _catalogDb;

    public StatusCommand(ControlPlaneDbContext catalogDb)
    {
        _catalogDb = catalogDb;
    }

    public string Name => ParsedArgs.SubcommandStatus;

    public async Task<int> ExecuteAsync(ParsedArgs args, CancellationToken ct)
    {
        var tenants = await _catalogDb
            .Tenants.AsNoTracking()
            .OrderBy(t => t.Slug)
            .Select(t => new
            {
                t.Id,
                t.Slug,
                t.Status,
                t.DbName,
                t.ProvisionedAt,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (tenants.Count == 0)
        {
            Console.Out.WriteLine("(no tenants registered)");
            return 0;
        }

        Console.Out.WriteLine($"{"slug", -24} {"status", -20} {"db_name", -36} provisioned_at");
        Console.Out.WriteLine(new string('-', 100));
        foreach (var t in tenants)
        {
            var stamp = t.ProvisionedAt?.ToString("O") ?? "—";
            Console.Out.WriteLine($"{t.Slug, -24} {t.Status, -20} {t.DbName, -36} {stamp}");
        }
        return 0;
    }
}
