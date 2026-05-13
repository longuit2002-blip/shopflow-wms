using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.Outbound.Infrastructure;

#pragma warning disable CA1707 // Identifiers should not contain underscores — EF migration class name encodes timestamp + descriptor

namespace ShopFlow.Outbound.Infrastructure.Migrations;

/// <summary>
/// Initial Outbound schema per Sprint-3-redux plan R2 — applied per-tenant.
/// Carries both <see cref="MigrationAttribute"/> and
/// <see cref="DbContextAttribute"/> per AGENTS.md §3.23; without them
/// <c>MigrateAsync()</c> is a silent no-op (see
/// <c>docs/solutions/2026-05-10-ef-migration-needs-attributes.md</c>).
/// </summary>
/// <remarks>
/// <para>Per ADR-0003 no business table carries <c>tenant_id</c>. The
/// idempotency anchor on <c>orders</c> is <c>UNIQUE(channel_external_order_id)</c>
/// per plan R1 — composite key catches duplicate POSTs at the index level.</para>
///
/// <para><c>saga_state</c> is created here as the table shape MassTransit's
/// EF saga repository will bind to in U4 (K15 smoke-build prerequisite).
/// The four columns match MassTransit v8.x's
/// <c>SagaStateMachineInstance</c> + <see cref="byte"/>[] <c>RowVersion</c>
/// convention: <c>CorrelationId uuid PK</c>, <c>CurrentState text</c>,
/// <c>RowVersion bytea</c>, <c>UpdatedAt timestamptz</c>. Column names use
/// PascalCase (deliberately, in double-quotes) because MassTransit's
/// default model snapshot maps to PascalCase property names without an
/// EF column-rename — keeping the Postgres column case-sensitive lets MT's
/// canonical EF mapping bind without per-column configuration in U4. Note
/// PostgreSQL lower-cases unquoted identifiers; the migration intentionally
/// quotes these column names.</para>
/// </remarks>
[DbContext(typeof(OutboundDbContext))]
[Migration("20260513000001_InitialOutboundSchema")]
public sealed partial class InitialOutboundSchema : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        // ---- orders ---------------------------------------------------------
        mb.CreateTable(
            name: "orders",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                channel_external_order_id = table.Column<string>(maxLength: 128, nullable: false),
                shipping_profile = table.Column<string>(maxLength: 64, nullable: false),
                status = table.Column<string>(maxLength: 32, nullable: false),
                expected_weight_total = table.Column<int>(nullable: true),
                actual_weight_total = table.Column<int>(nullable: true),
                label_url = table.Column<string>(maxLength: 512, nullable: true),
                tracking_number = table.Column<string>(maxLength: 128, nullable: true),
                pick_wave_id = table.Column<Guid>(nullable: true),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_orders", x => x.id)
        );

        mb.CreateIndex(
            name: "ux_orders_channel_external_order_id",
            table: "orders",
            column: "channel_external_order_id",
            unique: true
        );

        mb.CreateIndex(name: "ix_orders_status", table: "orders", column: "status");

        // ---- order_lines ----------------------------------------------------
        mb.CreateTable(
            name: "order_lines",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                order_id = table.Column<Guid>(nullable: false),
                sku = table.Column<string>(maxLength: 64, nullable: false),
                qty = table.Column<int>(nullable: false),
                expected_weight = table.Column<int>(nullable: true),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_order_lines", x => x.id);
                table.ForeignKey(
                    name: "fk_order_lines_orders",
                    column: x => x.order_id,
                    principalTable: "orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        mb.CreateIndex(
            name: "ix_order_lines_order_id_sku",
            table: "order_lines",
            columns: new[] { "order_id", "sku" }
        );

        // ---- pick_waves -----------------------------------------------------
        mb.CreateTable(
            name: "pick_waves",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                shipping_profile = table.Column<string>(maxLength: 64, nullable: false),
                picker_id = table.Column<string>(maxLength: 64, nullable: false),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
                closed_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_pick_waves", x => x.id)
        );

        // ---- pick_assignments ----------------------------------------------
        mb.CreateTable(
            name: "pick_assignments",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                pick_wave_id = table.Column<Guid>(nullable: false),
                order_id = table.Column<Guid>(nullable: false),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_pick_assignments", x => x.id);
                table.ForeignKey(
                    name: "fk_pick_assignments_pick_waves",
                    column: x => x.pick_wave_id,
                    principalTable: "pick_waves",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade
                );
                table.ForeignKey(
                    name: "fk_pick_assignments_orders",
                    column: x => x.order_id,
                    principalTable: "orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        mb.CreateIndex(
            name: "ix_pick_assignments_order_id",
            table: "pick_assignments",
            column: "order_id"
        );

        // ---- pickers (reference data) --------------------------------------
        mb.CreateTable(
            name: "pickers",
            columns: table => new
            {
                picker_id = table.Column<string>(maxLength: 64, nullable: false),
                display_name = table.Column<string>(maxLength: 128, nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_pickers", x => x.picker_id)
        );

        // ---- saga_state (MassTransit EF saga repo target — U4) -------------
        // The four canonical MT columns (CorrelationId / CurrentState /
        // RowVersion / UpdatedAt) are QUOTED PascalCase deliberately so
        // MassTransit's out-of-the-box EF mapping binds without per-column
        // configuration. PostgreSQL lower-cases unquoted identifiers; the
        // column-name strings below are emitted verbatim in CREATE TABLE.
        //
        // The per-state context fields (TenantId, shipping_profile, etc.)
        // are added inline so U4's saga writes don't need a follow-on
        // migration. These are lower_snake_case per the project convention
        // — the EF entity configuration declares the explicit HasColumnName
        // for each. version is the MT ISagaVersion counter.
        mb.CreateTable(
            name: "saga_state",
            columns: table => new
            {
                CorrelationId = table.Column<Guid>(nullable: false),
                CurrentState = table.Column<string>(maxLength: 64, nullable: false),
                RowVersion = table.Column<byte[]>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false),
                version = table.Column<int>(nullable: false, defaultValue: 0),
                tenant_id = table.Column<Guid>(nullable: false, defaultValueSql: "'00000000-0000-0000-0000-000000000000'"),
                shipping_profile = table.Column<string>(maxLength: 64, nullable: false, defaultValue: ""),
                line_count = table.Column<int>(nullable: false, defaultValue: 0),
                reserved_line_skus = table.Column<string>(maxLength: 2048, nullable: false, defaultValue: ""),
                released_line_skus = table.Column<string>(maxLength: 2048, nullable: false, defaultValue: ""),
                lines_awaiting_release = table.Column<int>(nullable: false, defaultValue: 0),
            },
            constraints: table => table.PrimaryKey("pk_saga_state", x => x.CorrelationId)
        );

        // ---- outbound_outbox_messages (per-module prefix per Sprint-2.5) ---
        mb.CreateTable(
            name: "outbound_outbox_messages",
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
            constraints: table => table.PrimaryKey("pk_outbound_outbox_messages", x => x.id)
        );

        mb.CreateIndex(
            name: "ix_outbound_outbox_messages_pending",
            table: "outbound_outbox_messages",
            columns: new[] { "processed_at", "created_at" }
        );
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable(name: "outbound_outbox_messages");
        mb.DropTable(name: "saga_state");
        mb.DropTable(name: "pickers");
        mb.DropTable(name: "pick_assignments");
        mb.DropTable(name: "pick_waves");
        mb.DropTable(name: "order_lines");
        mb.DropTable(name: "orders");
    }
}
