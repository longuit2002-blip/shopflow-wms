using Microsoft.EntityFrameworkCore;

namespace ShopFlow.Migrate.Modules;

/// <summary>
/// Describes one module's tenant-DB migration set. The CLI iterates the
/// registry inside <see cref="Provisioning.TenantProvisioner"/> and calls
/// <c>MigrateAsync()</c> against each module's DbContext bound to the
/// per-tenant connection string. U6 ships an empty tenant-side registry;
/// U8 adds <c>InventoryDbContext</c>; subsequent modules append.
/// </summary>
public sealed class ModuleMigrationDescriptor
{
    public ModuleMigrationDescriptor(
        string moduleName,
        Type dbContextType,
        string migrationsAssemblyName
    )
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            throw new ArgumentException("moduleName is required.", nameof(moduleName));
        }
        ArgumentNullException.ThrowIfNull(dbContextType);
        if (!typeof(DbContext).IsAssignableFrom(dbContextType))
        {
            throw new ArgumentException(
                $"dbContextType '{dbContextType.FullName}' is not a DbContext.",
                nameof(dbContextType)
            );
        }
        if (string.IsNullOrWhiteSpace(migrationsAssemblyName))
        {
            throw new ArgumentException(
                "migrationsAssemblyName is required.",
                nameof(migrationsAssemblyName)
            );
        }

        ModuleName = moduleName;
        DbContextType = dbContextType;
        MigrationsAssemblyName = migrationsAssemblyName;
    }

    public string ModuleName { get; }

    public Type DbContextType { get; }

    public string MigrationsAssemblyName { get; }
}
