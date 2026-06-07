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

// Postgres host port is config-driven (default 5432). A dev machine that already
// runs a native Postgres on 5432 can set DevStack:PostgresHostPort=5433 (env
// DevStack__PostgresHostPort or user-secret) to coexist; a clean clone with a
// free 5432 is unaffected. When overridden, the migrate executables below receive
// a matching Postgres__AdminConnectionString so their direct-DDL path follows the
// override too. (Closes the native-5432 coexistence item from the dev-stack note.)
var pgHostPort = int.TryParse(
    builder.Configuration["DevStack:PostgresHostPort"],
    out var configuredPgPort
)
    ? configuredPgPort
    : 5432;

// #4 — Minimal dev profile. DevStack:Minimal=true boots only what the
// Inventory-screen-end-to-end slice needs (postgres + pgbouncer + migrate +
// redis + rabbitmq + auth-api + inventory-api + gateway) and skips the
// observability stack, minio, mailpit, mocks, stocksync + notification. Lets
// the stack come up on a machine already running other containers — far fewer
// image pulls and no fixed-port collisions. Default false = full stack.
var minimalStack =
    bool.TryParse(builder.Configuration["DevStack:Minimal"], out var configuredMinimal)
    && configuredMinimal;

// Redis host port is config-driven (default 6379), same coexistence rationale
// as Postgres above: a machine already running a Redis on 6379 sets
// DevStack:RedisHostPort=6380.
var redisHostPort = int.TryParse(
    builder.Configuration["DevStack:RedisHostPort"],
    out var configuredRedisPort
)
    ? configuredRedisPort
    : 6379;

var postgres = builder
    .AddPostgres("postgres", userName: pgUser, password: pgPassword, port: pgHostPort)
    .WithDataVolume("shopflow-postgres-data")
    .WithEnvironment("POSTGRES_DB", "postgres")
    .WithEnvironment("POSTGRES_MAX_CONNECTIONS", "500");

// ── PgBouncer ───────────────────────────────────────────────────────────
// edoburu/pgbouncer runs VANILLA PgBouncer against a bind-mounted
// /etc/pgbouncer/pgbouncer.ini as-is when no DB_HOST/DATABASE_URL env vars are
// set — no auto-config. So the rendered [databases] (control-plane + every
// dev tenant) and userlist.txt from PgBouncerConfig.Render() are the config
// PgBouncer actually loads.
//
// This replaces bitnamilegacy/pgbouncer, whose entrypoint validated
// POSTGRESQL_* upstream vars and then auto-generated a postgres-ONLY
// pgbouncer.ini in /opt/bitnami/..., clobbering the bind-mounted one — so
// every service got `08P01: no such database: shopflow_control`. See
// docs/solutions/2026-05-27-aspire-dev-stack-first-boot-repairs.md.
//
// Mounted read-WRITE: the edoburu entrypoint chowns /etc/pgbouncer on startup
// so the unprivileged `postgres` runtime user can read the files.
//
// max_db_connections=20 per D1 + Tech Design v3.0 §1.6. Pool sizing comments
// live in the rendered pgbouncer.ini.template.
var pgBouncer = builder
    .AddContainer("pgbouncer", "edoburu/pgbouncer", "v1.25.1-p0")
    .WithEndpoint(port: 6432, targetPort: 6432, name: "tcp", scheme: "tcp")
    .WithBindMount(pgBouncerConfigDir, "/etc/pgbouncer", isReadOnly: false)
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
        workingDirectory: migrateProjectPath,
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
        workingDirectory: migrateProjectPath,
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
        workingDirectory: migrateProjectPath,
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

// Native-5432 coexistence + correct provisioning target. A clean clone with a
// free 5432 keeps the migrate appsettings path unchanged. But when the Aspire
// Postgres is relocated (DevStack:PostgresHostPort — needed on a machine that
// already runs a native Postgres on 5432), the migrate chain MUST follow the
// relocation for ALL THREE connections, not just admin. Otherwise catalog +
// tenant migrations apply against whatever sits on appsettings' localhost:5432
// (the native Postgres), while the services read the Aspire container through
// PgBouncer — they'd target different clusters and login fails with an empty
// catalog. TenantTemplate is the single source for migrate's apply-connection,
// the owner-seed connection, AND the db_connection_string stored in the catalog
// that services later use, so it must be a direct superuser connection at the
// relocated port. See docs/solutions/2026-05-27-aspire-dev-stack-first-boot-repairs.md.
if (pgHostPort != 5432)
{
    var directBase = $"Host=localhost;Port={pgHostPort};Username=postgres;Password=postgres";
    foreach (var m in new[] { migrateCatalog, migrateDev1, migrateDev2 })
    {
        m.WithEnvironment("Postgres__AdminConnectionString", $"{directBase};Database=postgres");
        m.WithEnvironment(
            "ControlPlane__ConnectionString",
            $"{directBase};Database=shopflow_control"
        );
        m.WithEnvironment("ControlPlane__TenantTemplate", $"{directBase};Database={{db}}");
    }
}

// ── Messaging + cache ───────────────────────────────────────────────────
var redis = builder.AddRedis("redis", port: redisHostPort);
var rabbitmq = builder.AddRabbitMQ("rabbitmq", port: 5672).WithManagementPlugin();
_ = redis;
_ = rabbitmq;

// ── Observability stack ─────────────────────────────────────────────────
// Logs → Seq, traces → Tempo via the OTel collector, metrics → Prometheus.
// Wire-up of the SharedKernel tracing exporter to point at the collector
// is U10 (CI + sign-off); for U7 the containers are declared so the dev
// dashboard surfaces them and operators can click through.
if (!minimalStack)
{
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
}

// ── Mock channel servers (Sprint-4 plan U7) ──────────────────────────
// Shopee mock runs as a sibling Kestrel-hosted ASP.NET process per
// Channel AGENTS.md §11.6 — separate process so integration tests
// exercise real HTTP + HMAC over the wire. Lazada/TikTok/Shopify mocks
// land in Sprint-6+ alongside their concrete adapters.
if (!minimalStack)
{
    var shopeeMock = builder
        .AddProject<Projects.ShopFlow_Mocks_Shopee>("shopee-mock")
        .WithExternalHttpEndpoints();
    _ = shopeeMock;

    // Finish-line U7 — Lazada mock, the second marketplace channel. Same
    // sibling-process posture as the Shopee mock; proves the plugin
    // architecture extends to a second channel with zero factory edits.
    var lazadaMock = builder
        .AddProject<Projects.ShopFlow_Mocks_Lazada>("lazada-mock")
        .WithExternalHttpEndpoints();
    _ = lazadaMock;
}

// ── Module APIs (Sprint-5 plan U8) ───────────────────────────────────
// StockSync.Api is the first module Api wired into the Aspire dev
// orchestrator. Waits for the tenant-provisioning chain (catalog + dev1
// + dev2) so the host can resolve every Ready tenant on startup. Phases
// 0-1 ship the modular monolith stage with each module Api running as
// its own Aspire resource; W6 split flips the Address values in
// src/ApiGateway/ShopFlow.Gateway/appsettings.json without changing the
// AddProject<> registrations here.
IResourceBuilder<ProjectResource>? stockSyncApi = null;
if (!minimalStack)
{
    stockSyncApi = builder
        .AddProject<Projects.ShopFlow_StockSync_Api>("stocksync-api")
        .WithReference(postgres)
        .WithReference(rabbitmq)
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
        .WithHttpEndpoint(name: "http")
        .WaitForCompletion(migrateDev2);
}

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
    // No launchSettings ship in-repo, so ASPNETCORE_ENVIRONMENT would default to
    // Production and the Sprint-9 KTD7 ForwardedHeaders guard (KnownProxies/
    // KnownNetworks must be set in non-Development) would crash every module API
    // at startup. The Aspire dev orchestrator IS Development — say so explicitly.
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    // No launch profile in-repo means Aspire injects no ASPNETCORE_URLS, so
    // every project would fall back to Kestrel's default :5000 and collide.
    // An explicit endpoint makes Aspire allocate a unique port + inject the URL.
    .WithHttpEndpoint(name: "http")
    .WithExternalHttpEndpoints();
_ = authApi;

// ── #4 — Inventory.Api + Gateway (Inventory screen end-to-end) ───────────
// Inventory.Api serves /api/v1/inventory/**. WithReference(postgres) keeps
// parity with the other module resources; the module actually talks to
// PgBouncer (localhost:6432) via ControlPlane:ConnectionString. Waits on the
// provisioning chain so ITenantCatalog can resolve dev1/dev2 on first request.
var inventoryApi = builder
    .AddProject<Projects.ShopFlow_Inventory_Api>("inventory")
    .WithReference(postgres)
    .WithReference(rabbitmq)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithHttpEndpoint(name: "http")
    .WaitForCompletion(migrateDev2);
_ = inventoryApi;

// YARP Gateway — the single front door the web dev-proxy targets at host:8080
// (web/vite.config.ts proxies /api/* + /auth/* here). The module APIs run as
// Aspire host processes on DYNAMIC ports; rather than add the ServiceDiscovery
// package we inject each resolved endpoint URL straight into the gateway's YARP
// config via env-var overrides (ASP.NET binds
// ReverseProxy:Clusters:<id>:Destinations:primary:Address). Static addresses,
// no new dependency, always correct for the live run.
var gateway = builder
    .AddProject<Projects.ShopFlow_Gateway>("gateway")
    .WithHttpEndpoint(port: 8080, name: "web")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithExternalHttpEndpoints()
    .WithEnvironment(
        "ReverseProxy__Clusters__auth__Destinations__primary__Address",
        authApi.GetEndpoint("http")
    )
    .WithEnvironment(
        "ReverseProxy__Clusters__inventory__Destinations__primary__Address",
        inventoryApi.GetEndpoint("http")
    )
    .WaitFor(authApi)
    .WaitFor(inventoryApi);

// stocksync only exists in the full stack; inject its address only then.
if (stockSyncApi is not null)
{
    gateway.WithEnvironment(
        "ReverseProxy__Clusters__stocksync__Destinations__primary__Address",
        stockSyncApi.GetEndpoint("http")
    );
}
_ = gateway;

// ── Mailpit (Sprint-9.5 U4) ─────────────────────────────────────────────
// Dev SMTP target for the Notification module. Tag pinned per KTD7
// (AGENTS.md rule 56 — no :latest). Exposes the SMTP port on 1025 +
// the browsable web UI on 8025; appsettings.Development.json points
// MailKitSmtp at host "mailpit" port 1025 so Aspire DNS resolves the
// container alias. Prod swaps in a real SMTP provider (Sendgrid / SES /
// Resend) via env-var overrides — Mailpit is NOT deployed to prod.
if (!minimalStack)
{
    var mailpit = builder
        .AddContainer("mailpit", "axllent/mailpit", "v1.21.0")
        .WithEndpoint(port: 1025, targetPort: 1025, name: "smtp", scheme: "tcp")
        .WithEndpoint(port: 8025, targetPort: 8025, name: "web", scheme: "http");
    _ = mailpit;

    // Sprint-9.5 U4 — Notification.Api hosted-service host. No REST surface
    // beyond /health; consumes the four Sprint-9 cross-module Auth events
    // via MassTransit and dispatches via SMTP through Mailpit (dev) or
    // real provider (prod). Waits on the tenant-provisioning chain so the
    // dispatcher has Ready tenants to iterate at startup.
    var notificationApi = builder
        .AddProject<Projects.ShopFlow_Notification_Api>("notification-api")
        .WithReference(postgres)
        .WithReference(rabbitmq)
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
        .WithHttpEndpoint(name: "http")
        .WaitFor(mailpit)
        .WaitForCompletion(migrateDev2);
    _ = notificationApi;
}

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
