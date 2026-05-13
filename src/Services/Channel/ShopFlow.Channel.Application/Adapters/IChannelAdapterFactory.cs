namespace ShopFlow.Channel.Application.Adapters;

/// <summary>
/// Resolves <see cref="IChannelAdapter"/> instances by channel type per
/// Sprint-4 plan R1/U5. Implementations are registered via
/// <c>AddChannelModule</c>; Sprint-6 adds Lazada by appending one DI line
/// plus the Lazada adapter file — zero changes to this resolver shape.
/// </summary>
public interface IChannelAdapterFactory
{
    /// <summary>
    /// Resolve the adapter for <paramref name="channelType"/>. Throws
    /// <see cref="UnknownChannelTypeException"/> when nothing is registered
    /// — surfaces loudly during Sprint-6+ rollout misconfigurations rather
    /// than silently accepting traffic for an unsupported marketplace.
    /// </summary>
    IChannelAdapter ResolveFor(string channelType);

    /// <summary>
    /// Try-resolve variant for the receiver controller — returns null on
    /// unknown rather than throwing, so the receiver can return a clean
    /// 501 to the caller.
    /// </summary>
    IChannelAdapter? TryResolve(string channelType);
}

/// <summary>
/// Thrown by <see cref="IChannelAdapterFactory.ResolveFor"/> when the
/// requested channel type has no registered adapter. Sprint-6+ rollout
/// catches this via DI smoke tests rather than at runtime.
/// </summary>
public sealed class UnknownChannelTypeException : InvalidOperationException
{
    public UnknownChannelTypeException(string channelType)
        : base($"No channel adapter registered for type '{channelType}'.")
    {
        ChannelType = channelType;
    }

    public string ChannelType { get; }
}
