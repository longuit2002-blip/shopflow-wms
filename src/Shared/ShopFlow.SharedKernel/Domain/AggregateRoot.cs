namespace ShopFlow.SharedKernel.Domain;

/// <summary>
/// Aggregate roots inherit from <see cref="BaseEntity"/> and carry an EF Core
/// row-version token so optimistic concurrency control is the default for
/// admin-flavoured edits. Per AGENTS.md §4.24 only aggregate roots inherit
/// this base; child entities inside an aggregate inherit <see cref="BaseEntity"/>.
/// </summary>
public abstract class AggregateRoot : BaseEntity
{
    public byte[] RowVersion { get; protected set; } = Array.Empty<byte>();
}
