using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1707 // Identifiers should not contain underscores — EF migration class name encodes timestamp + descriptor

namespace ShopFlow.Channel.Infrastructure.Migrations;

/// <summary>
/// Initial Channel schema per Sprint-4 plan R3/R6/R9/U2 — applied per-tenant.
/// Carries both <see cref="MigrationAttribute"/> and
/// <see cref="DbContextAttribute"/> per AGENTS.md §3.23; without them
/// <c>MigrateAsync()</c> is a silent no-op (see
/// <c>docs/solutions/2026-05-10-ef-migration-needs-attributes.md</c>).
/// </summary>
/// <remarks>
/// <para>Per ADR-0003 no business table carries <c>tenant_id</c>. The
/// idempotency anchor on <c>webhook_events</c> is
/// <c>UNIQUE(channel_id, provider_event_id)</c> per Tech Design v3.0 §6 —
/// duplicates hit the index, the receiver catches 23505 and returns the
/// existing row (Sprint-1-redux <c>ReservationRepository</c> pattern).</para>
/// </remarks>
[DbContext(typeof(ChannelDbContext))]
[Migration("20260513000001_InitialChannelSchema")]
public sealed partial class InitialChannelSchema : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        // ---- channels (tenant-side adapter-routing projection) -------------
        mb.CreateTable(
            name: "channels",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                channel_type = table.Column<string>(maxLength: 32, nullable: false),
                status = table.Column<string>(maxLength: 16, nullable: false),
                disabled_at = table.Column<DateTime>(nullable: true),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_channels", x => x.id)
        );

        // ---- webhook_events (UNIQUE-23505 idempotency anchor) --------------
        mb.CreateTable(
            name: "webhook_events",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                channel_id = table.Column<Guid>(nullable: false),
                provider_event_id = table.Column<string>(maxLength: 200, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                signature_verified = table.Column<bool>(nullable: false),
                status = table.Column<string>(maxLength: 16, nullable: false),
                processed_at = table.Column<DateTime>(nullable: true),
                failure_reason = table.Column<string>(maxLength: 512, nullable: true),
                received_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_webhook_events", x => x.id)
        );

        mb.CreateIndex(
            name: "ux_webhook_events_channel_provider_event",
            table: "webhook_events",
            columns: new[] { "channel_id", "provider_event_id" },
            unique: true
        );

        // ---- product_mappings (UNIQUE(channel_id, external_sku)) -----------
        mb.CreateTable(
            name: "product_mappings",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                channel_id = table.Column<Guid>(nullable: false),
                external_sku = table.Column<string>(maxLength: 128, nullable: false),
                internal_sku = table.Column<string>(maxLength: 64, nullable: false),
                confidence_score = table.Column<decimal>(type: "numeric(3,2)", nullable: false),
                mapping_method = table.Column<string>(maxLength: 16, nullable: false),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_product_mappings", x => x.id);
                table.CheckConstraint(
                    "ck_product_mappings_method",
                    "mapping_method IN ('Exact', 'Fuzzy', 'Manual')"
                );
            }
        );

        mb.CreateIndex(
            name: "ux_product_mappings_channel_external_sku",
            table: "product_mappings",
            columns: new[] { "channel_id", "external_sku" },
            unique: true
        );

        // ---- channel_outbox_messages (per-module prefix per Sprint-2.5) ----
        mb.CreateTable(
            name: "channel_outbox_messages",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                tenant_id = table.Column<Guid>(nullable: false),
                event_type = table.Column<string>(maxLength: 256, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                trace_id = table.Column<string>(maxLength: 64, nullable: true),
                created_at = table.Column<DateTime>(nullable: false),
                processed_at = table.Column<DateTime>(nullable: true),
                retry_count = table.Column<int>(nullable: false),
                last_error = table.Column<string>(maxLength: 2048, nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_channel_outbox_messages", x => x.id)
        );

        mb.CreateIndex(
            name: "ix_channel_outbox_messages_pending",
            table: "channel_outbox_messages",
            columns: new[] { "processed_at", "created_at" }
        );
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable(name: "channel_outbox_messages");
        mb.DropTable(name: "product_mappings");
        mb.DropTable(name: "webhook_events");
        mb.DropTable(name: "channels");
    }
}
