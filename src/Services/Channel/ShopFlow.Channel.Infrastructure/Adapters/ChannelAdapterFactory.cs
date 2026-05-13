using ShopFlow.Channel.Application.Adapters;

namespace ShopFlow.Channel.Infrastructure.Adapters;

/// <summary>
/// Dictionary-backed <see cref="IChannelAdapterFactory"/> per Sprint-4 plan
/// U5. Receives every registered <see cref="IChannelAdapter"/> via DI
/// enumeration and indexes by channel type (case-insensitive ordinal so
/// "shopee" / "Shopee" / "SHOPEE" all resolve).
/// </summary>
public sealed class ChannelAdapterFactory : IChannelAdapterFactory
{
    private readonly IReadOnlyDictionary<string, IChannelAdapter> _adapters;

    public ChannelAdapterFactory(IEnumerable<IChannelAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        _adapters = adapters.ToDictionary(
            a => a.ChannelType,
            a => a,
            StringComparer.OrdinalIgnoreCase
        );
    }

    public IChannelAdapter ResolveFor(string channelType)
    {
        if (string.IsNullOrWhiteSpace(channelType))
        {
            throw new UnknownChannelTypeException(channelType ?? string.Empty);
        }
        return _adapters.TryGetValue(channelType, out var adapter)
            ? adapter
            : throw new UnknownChannelTypeException(channelType);
    }

    public IChannelAdapter? TryResolve(string channelType)
    {
        if (string.IsNullOrWhiteSpace(channelType))
        {
            return null;
        }
        return _adapters.TryGetValue(channelType, out var adapter) ? adapter : null;
    }
}
