using Microsoft.EntityFrameworkCore;

namespace ShopFlow.Channel.Infrastructure;

/// <summary>
/// Placeholder per-tenant DbContext for the Channel module (plan U9). No
/// DbSets yet — real entities + migration land in Phase-1 Sprint-2 along
/// with the raw-webhook persistence flow per AGENTS.md §6.39 (idempotent
/// receivers persist before enqueue).
/// </summary>
/// <remarks>
/// Constructed via <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/>
/// per request (AGENTS.md §3.17). The empty context locks the shape so
/// the module's <c>shopflow-migrate</c> registry slot is reserved
/// (registration lands when the first migration is authored — until
/// then there is no schema to apply).
/// </remarks>
public sealed class ChannelDbContext : DbContext
{
    public ChannelDbContext(DbContextOptions<ChannelDbContext> options)
        : base(options) { }
}
