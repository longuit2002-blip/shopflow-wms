using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ShopFlow.ControlPlane.Domain;
using ShopFlow.ControlPlane.Infrastructure;
using ShopFlow.Migrate;
using ShopFlow.Migrate.Modules;
using ShopFlow.Migrate.Provisioning;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Migrate.UnitTests.Provisioning;

public class TenantProvisionerTests
{
    private static MigrateOptions DefaultOptions() =>
        new(
            Postgres: new PostgresOptions(
                AdminConnectionString: "Host=admin",
                AppRoleName: "shopflow_app",
                AppRolePassword: "secret"
            ),
            ControlPlane: new ControlPlaneOptions(
                ConnectionString: "Host=catalog",
                TenantTemplate: "Host=tenant;Database={db}",
                DefaultRegion: "ap-southeast-1",
                DefaultTier: "free"
            ),
            Migrate: new MigrateRuntimeOptions(Concurrency: 4, DbNamePrefix: "shopflow_t_")
        );

    private static ControlPlaneDbContext NewCatalog()
    {
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w =>
                w.Ignore(
                    Microsoft
                        .EntityFrameworkCore
                        .Diagnostics
                        .InMemoryEventId
                        .TransactionIgnoredWarning
                )
            )
            .Options;
        return new ControlPlaneDbContext(options);
    }

    [Fact]
    public async Task Provision_new_tenant_drives_workflow_in_order()
    {
        using var catalog = NewCatalog();
        var admin = new FakePostgresAdmin();
        var registry = new ModuleMigrationRegistry();
        var provisioner = new TenantProvisioner(
            catalog,
            admin,
            registry,
            DefaultOptions(),
            NullLogger<TenantProvisioner>.Instance
        );

        var outcome = await provisioner.ProvisionAsync("acme", CancellationToken.None);

        outcome.Should().Be(ProvisionOutcome.Provisioned);

        admin
            .Calls.Should()
            .ContainInOrder(
                "EnsureLoginRole(shopflow_app)",
                "DatabaseExists(shopflow_t_acme)",
                "CreateDatabase(shopflow_t_acme)",
                "Grant(shopflow_t_acme,shopflow_app)"
            );

        var tenant = await catalog.Tenants.FirstAsync(t => t.Slug == "acme");
        tenant.Status.Should().Be(TenantStatus.Ready);
        tenant.DbName.Should().Be("shopflow_t_acme");
        tenant.ProvisionedAt.Should().NotBeNull();

        (await catalog.TenantEvents.CountAsync(e => e.EventType == "tenant.provisioned"))
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task Provision_already_ready_tenant_is_a_noop()
    {
        using var catalog = NewCatalog();
        SeedReadyTenant(catalog, "acme");
        var admin = new FakePostgresAdmin();
        var provisioner = new TenantProvisioner(
            catalog,
            admin,
            new ModuleMigrationRegistry(),
            DefaultOptions(),
            NullLogger<TenantProvisioner>.Instance
        );

        var outcome = await provisioner.ProvisionAsync("acme", CancellationToken.None);

        outcome.Should().Be(ProvisionOutcome.AlreadyReady);
        admin.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Provision_resumes_from_failed_state()
    {
        using var catalog = NewCatalog();
        var tenant = Tenant.Create("acme", "shopflow_t_acme", "ap-southeast-1", "free").Value!;
        tenant.BeginProvisioning();
        tenant.MarkProvisioningFailed("postgres timeout");
        catalog.Tenants.Add(tenant);
        await catalog.SaveChangesAsync();

        var admin = new FakePostgresAdmin();
        var provisioner = new TenantProvisioner(
            catalog,
            admin,
            new ModuleMigrationRegistry(),
            DefaultOptions(),
            NullLogger<TenantProvisioner>.Instance
        );

        var outcome = await provisioner.ProvisionAsync("acme", CancellationToken.None);

        outcome.Should().Be(ProvisionOutcome.Resumed);
        var refreshed = await catalog.Tenants.FirstAsync(t => t.Slug == "acme");
        refreshed.Status.Should().Be(TenantStatus.Ready);
        refreshed.LastFailureReason.Should().BeNull();
    }

    [Fact]
    public async Task Provision_failure_marks_tenant_provisioning_failed()
    {
        using var catalog = NewCatalog();
        var admin = new FakePostgresAdmin
        {
            CreateDatabaseHook = _ =>
                throw new InvalidOperationException("simulated postgres ECONNREFUSED"),
        };
        var provisioner = new TenantProvisioner(
            catalog,
            admin,
            new ModuleMigrationRegistry(),
            DefaultOptions(),
            NullLogger<TenantProvisioner>.Instance
        );

        var act = () => provisioner.ProvisionAsync("acme", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();

        var tenant = await catalog.Tenants.FirstAsync(t => t.Slug == "acme");
        tenant.Status.Should().Be(TenantStatus.ProvisioningFailed);
        tenant.LastFailureReason.Should().Contain("ECONNREFUSED");
    }

    [Fact]
    public async Task Provision_skips_create_when_database_already_exists()
    {
        using var catalog = NewCatalog();
        var admin = new FakePostgresAdmin();
        admin.Databases.Add("shopflow_t_acme");
        var provisioner = new TenantProvisioner(
            catalog,
            admin,
            new ModuleMigrationRegistry(),
            DefaultOptions(),
            NullLogger<TenantProvisioner>.Instance
        );

        await provisioner.ProvisionAsync("acme", CancellationToken.None);

        admin.Calls.Should().NotContain("CreateDatabase(shopflow_t_acme)");
        admin.Calls.Should().Contain("Grant(shopflow_t_acme,shopflow_app)");
    }

    [Fact]
    public async Task Provision_rejects_archived_tenant()
    {
        using var catalog = NewCatalog();
        var tenant = Tenant.Create("acme", "shopflow_t_acme", "ap-southeast-1", "free").Value!;
        tenant.BeginProvisioning();
        tenant.MarkProvisioned();
        tenant.BeginArchiving();
        tenant.CompleteArchiving();
        catalog.Tenants.Add(tenant);
        await catalog.SaveChangesAsync();

        var provisioner = new TenantProvisioner(
            catalog,
            new FakePostgresAdmin(),
            new ModuleMigrationRegistry(),
            DefaultOptions(),
            NullLogger<TenantProvisioner>.Instance
        );

        var act = () => provisioner.ProvisionAsync("acme", CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot provision*Archived*");
    }

    [Fact]
    public async Task Provision_normalises_slug_to_lowercase()
    {
        using var catalog = NewCatalog();
        var provisioner = new TenantProvisioner(
            catalog,
            new FakePostgresAdmin(),
            new ModuleMigrationRegistry(),
            DefaultOptions(),
            NullLogger<TenantProvisioner>.Instance
        );

        await provisioner.ProvisionAsync("ACME", CancellationToken.None);

        (await catalog.Tenants.AnyAsync(t => t.Slug == "acme")).Should().BeTrue();
    }

    private static void SeedReadyTenant(ControlPlaneDbContext db, string slug)
    {
        var tenant = Tenant.Create(slug, $"shopflow_t_{slug}", "ap-southeast-1", "free").Value!;
        tenant.BeginProvisioning();
        tenant.MarkProvisioned();
        db.Tenants.Add(tenant);
        db.SaveChanges();
    }
}
