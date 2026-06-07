using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShopFlow.Notification.Domain.Entities;
using ShopFlow.Notification.Infrastructure.EntityConfigurations;

namespace ShopFlow.Notification.Infrastructure;

/// <summary>
/// EF Core context for one tenant's Notification schema. Constructed
/// per request from the AddNotificationModule (U3) lambda bound to
/// <c>IRequestContext.DbConnectionString</c> (AGENTS.md §3.17). Direct
/// <c>new NotificationDbContext(...)</c> outside a registration lambda /
/// *Factory / *Tests / *Fixture type is forbidden by ShopFlow0003.
/// </summary>
/// <remarks>
/// <para>Sprint-9.5 U1 ships three tables — <c>notification_outbox</c>
/// (rendered emails awaiting dispatch), <c>notification_log</c> (terminal
/// success, KTD3 UNIQUE dedup anchor on <c>(source_event_id,
/// recipient_email)</c>), and <c>notification_dead_letter</c> (terminal
/// failure with JSON payload for replay). The Notification module does
/// not emit its own cross-module events in Sprint-9.5 — no outbox-
/// dispatcher table here (the multiplexed dispatcher reads
/// <c>notification_outbox</c> in the email-queue sense, NOT the
/// MT-publish sense).</para>
/// <para>Per ADR-0003 no <c>tenant_id</c> column on any table; the
/// database identity IS the tenant boundary. Per-row tenant lookup
/// (for dispatcher logging) goes through
/// <c>IRequestContext.TenantId</c> on the binding scope.</para>
/// </remarks>
public sealed class NotificationDbContext : DbContext
{
    public NotificationDbContext(DbContextOptions<NotificationDbContext> options)
        : base(options) { }

    /// <summary>
    /// Suppress EF Core 9's
    /// <see cref="RelationalEventId.PendingModelChangesWarning"/>. Hand-authored
    /// migrations (AGENTS.md §3.23) do not ship the
    /// <c>NotificationDbContextModelSnapshot.cs</c> companion that
    /// <c>dotnet ef migrations add</c> emits; without it EF compares the
    /// runtime model against an empty baseline and surfaces the entire
    /// model as "pending changes". Schema correctness is enforced by
    /// the migration smoke tests. See
    /// <c>docs/solutions/2026-05-13-ef9-pendingmodelchangeswarning-with-hand-authored-migrations.md</c>.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(RelationalEventId.PendingModelChangesWarning)
        );
    }

    public DbSet<NotificationOutboxEntry> NotificationOutbox => Set<NotificationOutboxEntry>();

    public DbSet<NotificationLogEntry> NotificationLog => Set<NotificationLogEntry>();

    public DbSet<NotificationDeadLetterEntry> NotificationDeadLetter =>
        Set<NotificationDeadLetterEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new NotificationOutboxEntryConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationLogEntryConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationDeadLetterEntryConfiguration());
    }
}
