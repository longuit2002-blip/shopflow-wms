using System.Globalization;
using System.Text;

namespace ShopFlow.AppHost;

/// <summary>
/// Renders the PgBouncer transaction-pooling config + userlist for dev.
/// </summary>
/// <remarks>
/// <para>
/// Reads <c>infrastructure/pgbouncer/pgbouncer.ini.template</c>, substitutes
/// the dev-tenant database list and the auth file path, and writes the
/// rendered files to a transient run directory. The directory path is
/// returned so the AppHost can bind-mount it into the PgBouncer container at
/// <c>/etc/pgbouncer/</c>.
/// </para>
/// <para>
/// The same template ships unchanged to <c>infrastructure/pgbouncer/</c>
/// for the prod handoff via <c>docker-compose.yml</c>; the production deploy
/// pipeline runs the same substitution logic against the real tenant catalog
/// to materialize the prod pgbouncer.ini.
/// </para>
/// </remarks>
internal static class PgBouncerConfig
{
    private const string AdminUser = "shopflow_app";
    private const string AdminPassword = "shopflow_app_dev_only";
    private const string PostgresHostInsideNetwork = "postgres";
    private const int PostgresPortInsideNetwork = 5432;

    /// <summary>
    /// Render pgbouncer.ini + userlist.txt into a per-run temp directory and
    /// return its absolute path. Caller is responsible for bind-mounting the
    /// directory into the PgBouncer container as <c>/etc/pgbouncer/</c>.
    /// </summary>
    public static string Render(
        string templatePath,
        IReadOnlyCollection<string> tenantDbNames,
        string controlPlaneDbName = "shopflow_control"
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        ArgumentNullException.ThrowIfNull(tenantDbNames);

        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException(
                $"pgbouncer template not found at '{templatePath}'.",
                templatePath
            );
        }

        var runDir = Path.Combine(
            Path.GetTempPath(),
            "shopflow-pgbouncer",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)
        );
        Directory.CreateDirectory(runDir);

        var databases = BuildDatabasesSection(controlPlaneDbName, tenantDbNames);

        var rendered = File.ReadAllText(templatePath)
            .Replace("{databases}", databases, StringComparison.Ordinal)
            .Replace("{auth_file}", "/etc/pgbouncer/userlist.txt", StringComparison.Ordinal)
            .Replace("{admin_users}", AdminUser, StringComparison.Ordinal);

        File.WriteAllText(Path.Combine(runDir, "pgbouncer.ini"), rendered);
        File.WriteAllText(
            Path.Combine(runDir, "userlist.txt"),
            $"\"{AdminUser}\" \"{AdminPassword}\"\n"
        );

        return runDir;
    }

    private static string BuildDatabasesSection(
        string controlPlaneDbName,
        IReadOnlyCollection<string> tenantDbNames
    )
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{controlPlaneDbName} = host={PostgresHostInsideNetwork} port={PostgresPortInsideNetwork} dbname={controlPlaneDbName}"
            )
        );

        foreach (var db in tenantDbNames)
        {
            sb.AppendLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{db} = host={PostgresHostInsideNetwork} port={PostgresPortInsideNetwork} dbname={db}"
                )
            );
        }

        return sb.ToString().TrimEnd();
    }
}
