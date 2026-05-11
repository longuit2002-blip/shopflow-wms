using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShopFlow.ControlPlane.Infrastructure;
using ShopFlow.Migrate;
using ShopFlow.Migrate.Commands;
using ShopFlow.Migrate.Modules;
using ShopFlow.Migrate.Provisioning;

// ─────────────────────────────────────────────────────────────────────────
// shopflow-migrate — per-tenant migration runner (plan U6).
//
// Entry point: parse args, build a Generic-Host scope with the required
// services, resolve the matching ICommand, return its exit code.
// ─────────────────────────────────────────────────────────────────────────

var parse = ArgParser.Parse(args);

if (parse.ShowHelp)
{
    WriteHelp();
    return 0;
}

if (!parse.IsOk)
{
    Console.Error.WriteLine(parse.ErrorMessage);
    WriteHelp();
    return 2;
}

var parsed = parse.Args!;

using var host = BuildHost(args);
using var scope = host.Services.CreateScope();

var commands = scope.ServiceProvider.GetServices<ICommand>().ToList();
var command = commands.FirstOrDefault(c =>
    string.Equals(c.Name, parsed.Subcommand, StringComparison.Ordinal)
);
if (command is null)
{
    Console.Error.WriteLine($"no handler for subcommand '{parsed.Subcommand}'.");
    return 2;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    return await command.ExecuteAsync(parsed, cts.Token).ConfigureAwait(false);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("cancelled.");
    return 130;
}
catch (Exception ex)
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "shopflow-migrate {Subcommand} failed.", parsed.Subcommand);
    return 1;
}

static IHost BuildHost(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    var optionsRoot = builder.Configuration.Get<MigrateOptionsRoot>()
        ?? throw new InvalidOperationException(
            "appsettings.json is missing or malformed; expected Postgres, ControlPlane, Migrate sections."
        );
    var options = optionsRoot.ToOptions();

    builder.Services.AddSingleton(options);
    builder.Services.AddSingleton<IModuleMigrationRegistry>(_ => new ModuleMigrationRegistry());

    builder.Services.AddDbContext<ControlPlaneDbContext>(o =>
        o.UseNpgsql(
            options.ControlPlane.ConnectionString,
            npg => npg.MigrationsAssembly("ShopFlow.ControlPlane.Migrations")
        )
    );

    builder.Services.AddSingleton<IPostgresAdmin>(sp => new NpgsqlPostgresAdmin(
        options.Postgres.AdminConnectionString,
        sp.GetRequiredService<ILogger<NpgsqlPostgresAdmin>>()
    ));

    builder.Services.AddScoped<ITenantProvisioner, TenantProvisioner>();

    builder.Services.AddScoped<ICommand, ProvisionCommand>();
    builder.Services.AddScoped<ICommand, ApplyCommand>();
    builder.Services.AddScoped<ICommand, ArchiveCommand>();
    builder.Services.AddScoped<ICommand, RestoreCommand>();
    builder.Services.AddScoped<ICommand, StatusCommand>();

    return builder.Build();
}

static void WriteHelp()
{
    Console.Out.WriteLine(
        """
        shopflow-migrate — per-tenant migration runner

        Usage:
          shopflow-migrate provision --catalog
          shopflow-migrate provision --tenant=<slug>
          shopflow-migrate apply [--target=<version>] [--concurrency=<N>]
          shopflow-migrate archive --tenant=<slug>
          shopflow-migrate restore --tenant=<slug>
          shopflow-migrate status

        Configuration:
          Reads appsettings.json next to the executable plus environment
          variables (double-underscore section delimiter, e.g.
          Postgres__AdminConnectionString=...).
        """
    );
}

/// <summary>
/// Configuration binding shape — mirrors the JSON sections in
/// <c>appsettings.json</c>. Lives in <c>Program</c> rather than next to
/// <see cref="MigrateOptions"/> so the public record stays a simple immutable
/// transfer object without binding annotations.
/// </summary>
internal sealed class MigrateOptionsRoot
{
    public PostgresSection Postgres { get; set; } = new();

    public ControlPlaneSection ControlPlane { get; set; } = new();

    public MigrateSection Migrate { get; set; } = new();

    public MigrateOptions ToOptions() =>
        new(
            Postgres: new PostgresOptions(
                AdminConnectionString: Require(
                    Postgres.AdminConnectionString,
                    "Postgres:AdminConnectionString"
                ),
                AppRoleName: Require(Postgres.AppRoleName, "Postgres:AppRoleName"),
                AppRolePassword: Require(Postgres.AppRolePassword, "Postgres:AppRolePassword")
            ),
            ControlPlane: new ControlPlaneOptions(
                ConnectionString: Require(
                    ControlPlane.ConnectionString,
                    "ControlPlane:ConnectionString"
                ),
                TenantTemplate: RequireTemplate(ControlPlane.TenantTemplate),
                DefaultRegion: Require(ControlPlane.DefaultRegion, "ControlPlane:DefaultRegion"),
                DefaultTier: Require(ControlPlane.DefaultTier, "ControlPlane:DefaultTier")
            ),
            Migrate: new MigrateRuntimeOptions(
                Concurrency: Migrate.Concurrency <= 0 ? 4 : Migrate.Concurrency,
                DbNamePrefix: Require(Migrate.DbNamePrefix, "Migrate:DbNamePrefix")
            )
        );

    private static string Require(string? value, string key) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"configuration '{key}' is required.")
            : value;

    private static string RequireTemplate(string? value)
    {
        var v = Require(value, "ControlPlane:TenantTemplate");
        if (!v.Contains("{db}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "ControlPlane:TenantTemplate must contain the literal token '{db}'."
            );
        }
        return v;
    }

    internal sealed class PostgresSection
    {
        public string? AdminConnectionString { get; set; }

        public string? AppRoleName { get; set; }

        public string? AppRolePassword { get; set; }
    }

    internal sealed class ControlPlaneSection
    {
        public string? ConnectionString { get; set; }

        public string? TenantTemplate { get; set; }

        public string? DefaultRegion { get; set; }

        public string? DefaultTier { get; set; }
    }

    internal sealed class MigrateSection
    {
        public int Concurrency { get; set; }

        public string? DbNamePrefix { get; set; }
    }
}

/// <summary>Marker for the typed logger in <see cref="Program"/> (top-level statements have no class).</summary>
public partial class Program;
