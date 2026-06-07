using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using ShopFlow.ControlPlane.Domain;
using ShopFlow.ControlPlane.Infrastructure;
using ShopFlow.SharedKernel.Domain;

// ─────────────────────────────────────────────────────────────────────────
// shopflow-gate — phase-gate runner (plan U10).
//
// Usage:
//   shopflow-gate phase-0-redux
//
// Runs the operational gate checklist against a live control plane:
//   • Catalog DB reachable
//   • Catalog migrations current (__ef_migrations_history non-empty)
//   • Every tenant catalogued is Ready (no leftover Pending /
//     Provisioning rows from interrupted bootstraps)
//   • PgBouncer reachable on the configured connection string
//
// Each check prints PASS/FAIL with timing. Exit code 0 if all pass,
// non-zero if any fail.
// ─────────────────────────────────────────────────────────────────────────

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    WriteHelp();
    return 0;
}

if (!string.Equals(args[0], "phase-0-redux", StringComparison.Ordinal))
{
    Console.Error.WriteLine($"unknown gate '{args[0]}'.");
    WriteHelp();
    return 2;
}

using var host = BuildHost(args);

using var scope = host.Services.CreateScope();
var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
var options = scope.ServiceProvider.GetRequiredService<GateOptions>();
var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

var checks = new List<(string Name, Func<Task<(bool ok, string detail)>> Run)>
{
    ("catalog reachable", () => CheckCatalogReachable(db)),
    ("catalog migrated", () => CheckCatalogMigrated(db)),
    ("tenants Ready", () => CheckTenantsReady(db)),
    (
        "pgbouncer reachable",
        () => CheckPgBouncerReachable(options.PgBouncerConnectionString, logger)
    ),
};

var failures = 0;
Console.Out.WriteLine();
Console.Out.WriteLine("ShopFlow gate — phase-0-redux");
Console.Out.WriteLine(new string('=', 60));

foreach (var (name, run) in checks)
{
    var sw = Stopwatch.StartNew();
    try
    {
        var (ok, detail) = await run();
        sw.Stop();
        var status = ok ? "PASS" : "FAIL";
        Console.Out.WriteLine($"  [{status}] {name, -26} {sw.ElapsedMilliseconds, 6}ms — {detail}");
        if (!ok)
        {
            failures++;
        }
    }
    catch (Exception ex)
    {
        sw.Stop();
        failures++;
        Console.Out.WriteLine(
            $"  [FAIL] {name, -26} {sw.ElapsedMilliseconds, 6}ms — {ex.GetType().Name}: {ex.Message}"
        );
    }
}

Console.Out.WriteLine(new string('=', 60));
Console.Out.WriteLine(
    failures == 0
        ? "phase-0-redux gate: PASS"
        : $"phase-0-redux gate: FAIL ({failures} check(s) failed)"
);
Console.Out.WriteLine();

return failures == 0 ? 0 : 1;

static IHost BuildHost(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    var catalogConn =
        builder.Configuration.GetValue<string>("ControlPlane:ConnectionString")
        ?? throw new InvalidOperationException(
            "configuration 'ControlPlane:ConnectionString' is required."
        );
    var pgBouncerConn =
        builder.Configuration.GetValue<string>("PgBouncer:HealthCheckConnectionString")
        ?? catalogConn;

    builder.Services.AddSingleton(new GateOptions(catalogConn, pgBouncerConn));

    builder.Services.AddDbContext<ControlPlaneDbContext>(o =>
        o.UseNpgsql(catalogConn, npg => npg.MigrationsAssembly("ShopFlow.ControlPlane.Migrations"))
    );

    return builder.Build();
}

static void WriteHelp()
{
    Console.Out.WriteLine(
        """
        shopflow-gate — phase-gate runner

        Usage:
          shopflow-gate phase-0-redux

        Configuration (appsettings.json + env vars; ControlPlane__ConnectionString=...):
          ControlPlane:ConnectionString          — required; control-plane DB connection.
          PgBouncer:HealthCheckConnectionString  — optional; defaults to catalog connection.

        Phase-0-redux checks: catalog reachable, catalog migrated, all tenants Ready, PgBouncer reachable.
        Exit code: 0 if all pass, 1 otherwise. Returns 2 on usage error.
        """
    );
}

static async Task<(bool ok, string detail)> CheckCatalogReachable(ControlPlaneDbContext db)
{
    var canConnect = await db.Database.CanConnectAsync();
    return canConnect
        ? (true, "control-plane DB accepted a connection")
        : (false, "could not open a connection to control-plane DB");
}

static async Task<(bool ok, string detail)> CheckCatalogMigrated(ControlPlaneDbContext db)
{
    var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
    var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
    if (applied.Count == 0)
    {
        return (false, "no migrations applied (catalog is empty)");
    }
    return pending.Count == 0
        ? (true, $"{applied.Count} migration(s) applied; 0 pending")
        : (
            false,
            $"{pending.Count} pending migration(s); run shopflow-migrate provision --catalog"
        );
}

static async Task<(bool ok, string detail)> CheckTenantsReady(ControlPlaneDbContext db)
{
    var counts = new Dictionary<TenantStatus, int>();
    await foreach (var tenant in db.Tenants.AsAsyncEnumerable())
    {
        counts.TryGetValue(tenant.Status, out var n);
        counts[tenant.Status] = n + 1;
    }

    if (counts.Count == 0)
    {
        return (true, "0 tenants registered (cluster is freshly provisioned)");
    }

    var notReady = counts
        .Where(kv => kv.Key != TenantStatus.Ready)
        .Select(kv => $"{kv.Value}×{kv.Key}")
        .ToArray();

    if (notReady.Length == 0)
    {
        return (true, $"{counts[TenantStatus.Ready]} tenant(s) Ready, none in transition");
    }

    return (false, $"non-Ready tenants present: {string.Join(", ", notReady)}");
}

static async Task<(bool ok, string detail)> CheckPgBouncerReachable(
    string connString,
    ILogger logger
)
{
    try
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        var result = await cmd.ExecuteScalarAsync();
        return result is not null
            ? (true, "PgBouncer accepted a connection and SELECT 1 succeeded")
            : (false, "PgBouncer connection opened but SELECT 1 returned null");
    }
    catch (Exception ex)
    {
        logger.LogDebug(ex, "PgBouncer reachability check raised.");
        return (false, $"{ex.GetType().Name}: {ex.Message}");
    }
}

internal sealed record GateOptions(
    string ControlPlaneConnectionString,
    string PgBouncerConnectionString
);

/// <summary>Marker for the typed logger in <see cref="Program"/> (top-level statements have no class).</summary>
public partial class Program;
