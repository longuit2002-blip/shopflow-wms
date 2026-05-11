namespace ShopFlow.Migrate.Modules;

/// <summary>
/// Pluggable registry of per-tenant module migration sets. The
/// <c>TenantProvisioner</c> consumes this seam so additional modules (U8
/// Inventory, later Inbound/Outbound/Channel/Analytics) can be added without
/// touching the CLI command code.
/// </summary>
public interface IModuleMigrationRegistry
{
    IReadOnlyList<ModuleMigrationDescriptor> All { get; }
}
