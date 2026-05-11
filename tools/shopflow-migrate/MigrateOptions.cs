namespace ShopFlow.Migrate;

/// <summary>
/// Configuration bound from <c>appsettings.json</c> + environment variables.
/// Keys map to the JSON shape: <c>Postgres:*</c>, <c>ControlPlane:*</c>,
/// <c>Migrate:*</c>. Validated once at startup in <c>Program</c>; commands
/// consume the validated record.
/// </summary>
public sealed record MigrateOptions(
    PostgresOptions Postgres,
    ControlPlaneOptions ControlPlane,
    MigrateRuntimeOptions Migrate
);

/// <summary>
/// Connection details for the <em>superuser</em> path that issues DDL
/// (CREATE DATABASE, CREATE ROLE, REVOKE CONNECT). Bypasses PgBouncer
/// per AGENTS.md §3.20 — DDL is forbidden under transaction-pooling.
/// </summary>
public sealed record PostgresOptions(
    string AdminConnectionString,
    string AppRoleName,
    string AppRolePassword
);

/// <summary>
/// Catalog DB + tenant DB connection-string template. The template MUST
/// contain the literal token <c>{db}</c>; <c>TenantProvisioner</c> substitutes
/// the tenant's <c>db_name</c> to materialise per-tenant connections.
/// </summary>
public sealed record ControlPlaneOptions(
    string ConnectionString,
    string TenantTemplate,
    string DefaultRegion,
    string DefaultTier
);

public sealed record MigrateRuntimeOptions(int Concurrency, string DbNamePrefix);
