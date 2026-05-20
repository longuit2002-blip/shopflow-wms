using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using ShopFlow.AppHost;

// ─────────────────────────────────────────────────────────────────────────
// ShopFlow dev orchestrator (plan U7).
//
// Composes Postgres + PgBouncer + Redis + RabbitMQ + the observability
// stack, then chains three `shopflow-migrate` executables that provision
// the control-plane database plus two dev tenants (dev1, dev2) before any
// application service can start. Application services land in U8+; the
// AppHost reserves the dependency edges via WaitForCompletion on the
// migrate-dev2 resource so adding a service is a one-line WaitFor.
//
// Production handoff: same resources, same image tags, no Aspire. See
// infrastructure/docker-compose.yml. Aspire is dev-only per ADR-0001.
// ─────────────────────────────────────────────────────────────────────────

const string DevTenant1 = "dev1";
const string DevTenant2 = "dev2";

var builder = DistributedApplication.CreateBuilder(args);

var repoRoot = ResolveRepoRoot();
var pgBouncerTemplate = Path.Combine(
    repoRoot,
    "infrastructure",
    "pgbouncer",
    "pgbouncer.ini.template"
);
var pgBouncerConfigDir = PgBouncerConfig.Render(
    templatePath: pgBouncerTemplate,
    tenantDbNames: new[] { "shopflow_t_" + DevTenant1, "shopflow_t_" + DevTenant2 }
);

// ── Postgres ────────────────────────────────────────────────────────────
// `postgres` superuser + literal password match shopflow-migrate's
// appsettings.json (Postgres:AdminConnectionString) so the bootstrap chain
// can perform DDL bypassing PgBouncer. Production swaps in real secrets.
//
// max_connections=500 per D1 — the cluster fronts 25-50 tenants × pool
// budget; 500 leaves headroom for admin sessions and migration runs.
var pgPassword = builder.AddParameter("pg-superuser-pwd", "postgres", secret: true);
var pgUser = builder.AddParameter("pg-superuser-user", "postgres");

var postgres = builder
    .AddPostgres("postgres", userName: pgUser, password: pgPassword, port: 5432)
    .WithDataVolume("shopflow-postgres-data")
    .WithEnvironment("POSTGRES_DB", "postgres")
    .WithEnvironment("POSTGRES_MAX_CONNECTIONS", "500");

// ── PgBouncer ───────────────────────────────────────────────────────────
// bitnami/pgbouncer reads pgbouncer.ini from /bitnami/pgbouncer/conf/ when
// PGBOUNCER_USERLIST_FILE etc are unset and PGBOUNCER_INI_FILE points at a
// bind-mounted file. We bind-mount the rendered run-dir from
// PgBouncerConfig.Render() into /etc/pgbouncer/ and point PGBOUNCER_INI_FILE
// at the standard path that bitnami uses, so the dev container exercises
// the same config shape as the production handoff (infrastructure/pgbouncer/).
//
// max_db_connections=20 per D1 + Tech Design v3.0 §1.6. Pool sizing comments
// live in the rendered pgbouncer.ini.template.
var pgBouncer = builder
    .AddContainer("pgbouncer", "bitnami/pgbouncer", "1.23.1")
    .WithEndpoint(port: 6432, targetPort: 6432, name: "tcp", scheme: "tcp")
    .WithBindMount(pgBouncerConfigDir, "/etc/pgbouncer", isReadOnly: true)
    .WithEnvironment("PGBOUNCER_AUTH_TYPE", "scram-sha-256")
    .WithEnvironment("PGBOUNCER_INI_FILE", "/etc/pgbouncer/pgbouncer.ini")
    .WithEnvironment("PGBOUNCER_USERLIST_FILE", "/etc/pgbouncer/userlist.txt")
    .WaitFor(postgres);

// ── shopflow-migrate bootstrap chain ────────────────────────────────────
// Three one-shot executables that run `shopflow-migrate` in sequence:
//   1. provision --catalog  → creates shopflow_control + applies catalog migration
//   2. provision --tenant=dev1
//   3. provision --tenant=dev2
// Each waits for the previous to complete (exit 0). Application services
// added in later units can WaitFor(migrateDev2) to block on a hydrated
// catalog + ready dev tenants.
//
// The migrate CLI talks to PgBouncer for app-mode connections (port 6432)
// and directly to Postgres (port 5432) for DDL — appsettings.json holds
// both connection strings. Running `dotnet run --project` keeps the chain
// self-contained: no docker-build dance for the migrate tool in dev.
var migrateProjectPath = Path.Combine(repoRoot, "tools", "shopflow-migrate");

var migrateCatalog = builder
    .AddExecutable(
        name: "migrate-catalog",
        command: "dotnet",
        workingDirectory: repoRoot,
        args: new[]
        {
            "run",
            "--project",
            migrateProjectPath,
            "--no-build",
            "--",
            "provision",
            "--catalog",
        }
    )
    .WaitFor(pgBouncer);

var migrateDev1 = builder
    .AddExecutable(
        name: "migrate-dev1",
        command: "dotnet",
        workingDirectory: repoRoot,
        args: new[]
        {
            "run",
            "--project",
            migrateProjectPath,
            "--no-build",
            "--",
            "provision",
            $"--tenant={DevTenant1}",
        }
    )
    .WaitForCompletion(migrateCatalog);

var migrateDev2 = builder
    .AddExecutable(
        name: "migrate-dev2",
        command: "dotnet",
        workingDirectory: repoRoot,
        args: new[]
        {
            "run",
            "--project",
            migrateProjectPath,
            "--no-build",
            "--",
            "provision",
            $"--tenant={DevTenant2}",
        }
    )
    .WaitForCompletion(migrateDev1);

// Suppress the "unused" warning — migrateDev2 is the published readiness
// edge that later units (U8 Inventory.Api etc.) bind via WaitFor.
_ = migrateDev2;

// ── Messaging + cache ───────────────────────────────────────────────────
var redis = builder.AddRedis("redis", port: 6379);
var rabbitmq = builder.AddRabbitMQ("rabbitmq", port: 5672).WithManagementPlugin();
_ = redis;
_ = rabbitmq;

// ── Observability stack ─────────────────────────────────────────────────
// Logs → Seq, traces → Tempo via the OTel collector, metrics → Prometheus.
// Wire-up of the SharedKernel tracing exporter to point at the collector
// is U10 (CI + sign-off); for U7 the containers are declared so the dev
// dashboard surfaces them and operators can click through.
var seq = builder
    .AddContainer("seq", "datalust/seq", "2024.3")
    .WithEndpoint(port: 5341, targetPort: 80, name: "http", scheme: "http")
    .WithEnvironment("ACCEPT_EULA", "Y");

var prometheus = builder
    .AddContainer("prometheus", "prom/prometheus", "v2.55.1")
    .WithEndpoint(port: 9090, targetPort: 9090, name: "http", scheme: "http")
    .WithBindMount(
        Path.Combine(repoRoot, "infrastructure", "observability", "prometheus.yml"),
        "/etc/prometheus/prometheus.yml",
        isReadOnly: true
    );

var tempo = builder
    .AddContainer("tempo", "grafana/tempo", "2.6.1")
    .WithArgs("-config.file=/etc/tempo/tempo.yaml")
    .WithEndpoint(port: 3200, targetPort: 3200, name: "http", scheme: "http")
    .WithEndpoint(port: 4317, targetPort: 4317, name: "otlp-grpc", scheme: "tcp")
    .WithBindMount(
        Path.Combine(repoRoot, "infrastructure", "observability", "tempo.yaml"),
        "/etc/tempo/tempo.yaml",
        isReadOnly: true
    );

var otelCollector = builder
    .AddContainer("otel-collector", "otel/opentelemetry-collector-contrib", "0.111.0")
    .WithArgs("--config=/etc/otel/collector.yaml")
    .WithEndpoint(port: 4318, targetPort: 4318, name: "otlp-http", scheme: "http")
    .WithBindMount(
        Path.Combine(repoRoot, "infrastructure", "observability", "otel-collector.yaml"),
        "/etc/otel/collector.yaml",
        isReadOnly: true
    )
    .WaitFor(tempo)
    .WaitFor(seq);

var minio = builder
    .AddContainer("minio", "minio/minio", "RELEASE.2024-10-29T16-01-48Z")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithEndpoint(port: 9000, targetPort: 9000, name: "api", scheme: "http")
    .WithEndpoint(port: 9001, targetPort: 9001, name: "console", scheme: "http")
    .WithEnvironment("MINIO_ROOT_USER", "shopflow")
    .WithEnvironment("MINIO_ROOT_PASSWORD", "shopflow_dev_only");

_ = (seq, prometheus, tempo, otelCollector, minio);

// ── Mock channel servers (Sprint-4 plan U7) ──────────────────────────
// Shopee mock runs as a sibling Kestrel-hosted ASP.NET process per
// Channel AGENTS.md §11.6 — separate process so integration tests
// exercise real HTTP + HMAC over the wire. Lazada/TikTok/Shopify mocks
// land in Sprint-6+ alongside their concrete adapters.
var shopeeMock = builder
    .AddProject<Projects.ShopFlow_Mocks_Shopee>("shopee-mock")
    .WithExternalHttpEndpoints();
_ = shopeeMock;

// ── Module APIs (Sprint-5 plan U8) ───────────────────────────────────
// StockSync.Api is the first module Api wired into the Aspire dev
// orchestrator. Waits for the tenant-provisioning chain (catalog + dev1
// + dev2) so the host can resolve every Ready tenant on startup. Phases
// 0-1 ship the modular monolith stage with each module Api running as
// its own Aspire resource; W6 split flips the Address values in
// src/ApiGateway/ShopFlow.Gateway/appsettings.json without changing the
// AddProject<> registrations here.
var stockSyncApi = builder
    .AddProject<Projects.ShopFlow_StockSync_Api>("stocksync-api")
    .WithReference(postgres)
    .WithReference(rabbitmq)
    .WaitForCompletion(migrateDev2);
_ = stockSyncApi;

// Sprint-8 U9 — real auth surface. AddControlPlane needs the postgres
// resource so the tenant catalog connection string flows in via
// Aspire env injection. AddAuthModule needs the redis resource for
// the refresh-token store. Without these references the API throws
// at first request when ITenantCatalog / IConnectionMultiplexer
// resolution fails.
var authApi = builder
    .AddProject<Projects.ShopFlow_Auth_Api>("auth-api")
    .WithReference(postgres)
    .WithReference(redis)
    .WithExternalHttpEndpoints();
_ = authApi;

await builder.Build().RunAsync().ConfigureAwait(false);

static string ResolveRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "ShopFlow.sln")))
        {
            return dir.FullName;
        }
        dir = dir.Parent;
    }
    throw new InvalidOperationException(
        "Could not locate ShopFlow.sln by walking up from AppContext.BaseDirectory. "
            + "The AppHost must run from within the source tree."
    );
}
