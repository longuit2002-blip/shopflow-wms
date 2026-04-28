namespace ShopFlow.SharedKernel.Domain;

/// <summary>
/// Base class for value objects: structural equality over the declared
/// equality components, immutability is the implementor's responsibility.
/// Tech Design §20 verbatim, with a stable hash fold that is order-sensitive.
/// </summary>
public abstract class ValueObject
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public override bool Equals(object? obj) =>
        obj is ValueObject other
        && other.GetType() == GetType()
        && GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());

    public override int GetHashCode() =>
        GetEqualityComponents()
            .Aggregate(0, (acc, o) => HashCode.Combine(acc, o?.GetHashCode() ?? 0));

    public static bool operator ==(ValueObject? left, ValueObject? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}
