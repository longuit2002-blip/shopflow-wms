namespace ShopFlow.Migrate.Modules;

/// <summary>
/// Mutable list-backed registry. Composition-root code calls
/// <see cref="Register"/> for every module DbContext that participates in
/// tenant-DB migrations. Duplicate (by <c>DbContextType</c>) registrations
/// are rejected to surface bootstrap typos loudly.
/// </summary>
public sealed class ModuleMigrationRegistry : IModuleMigrationRegistry
{
    private readonly List<ModuleMigrationDescriptor> _descriptors = new();

    public IReadOnlyList<ModuleMigrationDescriptor> All => _descriptors;

    public ModuleMigrationRegistry Register(ModuleMigrationDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (_descriptors.Any(d => d.DbContextType == descriptor.DbContextType))
        {
            throw new InvalidOperationException(
                $"DbContext '{descriptor.DbContextType.FullName}' is already registered."
            );
        }

        _descriptors.Add(descriptor);
        return this;
    }
}
