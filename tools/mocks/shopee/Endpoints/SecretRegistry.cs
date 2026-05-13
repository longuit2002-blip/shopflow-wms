using System.Collections.Concurrent;

namespace ShopFlow.Mocks.Shopee.Endpoints;

/// <summary>
/// In-memory channel_id → secret bytes map per Sprint-4 plan U7. Seeded
/// from appsettings at startup; <c>POST /__seed-channel</c> lets tests add
/// runtime entries.
/// </summary>
public sealed class SecretRegistry
{
    private readonly ConcurrentDictionary<Guid, byte[]> _secrets = new();

    public void Register(Guid channelId, byte[] secret) => _secrets[channelId] = secret;

    public byte[]? Get(Guid channelId) =>
        _secrets.TryGetValue(channelId, out var secret) ? secret : null;
}

public sealed record SeedChannelRequest(Guid ChannelId, string SecretUtf8);
