using Microsoft.EntityFrameworkCore;

namespace ShopFlow.Outbound.Infrastructure;

/// <summary>
/// Placeholder per-tenant DbContext for the Outbound module (plan U9). No
/// DbSets yet — real entities + migration land in Phase-1 Sprint-3 along
/// with the saga state-machine tables for the fulfillment pipeline
/// Reserve → Pick → Pack → Ship per Tech Design v3.0 §9.
/// </summary>
/// <remarks>
/// Constructed via <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/>
/// per request (AGENTS.md §3.17). The empty context locks the shape so
/// the module's <c>shopflow-migrate</c> registry slot is reserved
/// (registration lands when the first migration is authored — until
/// then there is no schema to apply).
/// </remarks>
public sealed class OutboundDbContext : DbContext
{
    public OutboundDbContext(DbContextOptions<OutboundDbContext> options)
        : base(options) { }
}
