using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.Outbound.Infrastructure;

#pragma warning disable CA1707 // Identifiers should not contain underscores — EF migration class name encodes timestamp + descriptor

namespace ShopFlow.Outbound.Infrastructure.Migrations;

/// <summary>
/// Sprint-7 U1 — adds the <c>outbound_saga_transitions</c> audit table to
/// the Outbound tenant schema. Sprint-7 R14: the saga's
/// <c>IStateObserver&lt;FulfillmentSagaState&gt;</c> writes one row per
/// TransitionTo, recording the from/to states, wall-clock occurred_at,
/// CLR-name of the triggering event, and W3C TraceContext correlation_id.
/// </summary>
/// <remarks>
/// <para>Carries both <see cref="MigrationAttribute"/> and
/// <see cref="DbContextAttribute"/> per AGENTS.md §3.23 — without both
/// <c>MigrateAsync()</c> is a silent no-op
/// (<c>docs/solutions/2026-05-10-ef-migration-needs-attributes.md</c>).</para>
///
/// <para>Module-name prefix per Sprint-2.5 — the table is named
/// <c>outbound_saga_transitions</c> rather than <c>saga_transitions</c>
/// so it cannot collide with a future module's identically-named concept
/// when all module tables share one physical tenant DB
/// (<c>docs/solutions/2026-05-13-cross-module-outbox-table-name-collision.md</c>).</para>
///
/// <para>Index on <c>(order_id, occurred_at)</c> serves R15's
/// <c>GET /api/outbound/orders/{id}/transitions</c> query — list all
/// transitions for one order ordered chronologically. Per ADR-0003 no
/// <c>tenant_id</c> column.</para>
/// </remarks>
[DbContext(typeof(OutboundDbContext))]
// Finish-line U4 — renamed from 20260519100001 → 20260519000001 to FIX a
// migration-ordering bug: this migration CREATES outbound_saga_transitions, but
// AddUniqueOnSagaTransitions (20260519000002) + AddActorUserIdToSagaTransitions
// reference that table — and at the old ID this table-creation sorted AFTER them
// (100001 > 000002), so MigrateAsync threw 42P01 "relation
// outbound_saga_transitions does not exist". AddOrderTransitions (Sprint-7) was
// authored before the Sprint-7.5 dependents; it now sorts first (right after
// InitialOutboundSchema), matching authorship chronology. Never applied to a
// durable DB (the Outbound integration suite never ran end-to-end), so the
// rename is safe — same fix class as the Sprint-13 AddPackerRole correction.
[Migration("20260519000001_AddOrderTransitions")]
public sealed partial class AddOrderTransitions : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable(
            name: "outbound_saga_transitions",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                order_id = table.Column<Guid>(nullable: false),
                from_state = table.Column<string>(maxLength: 64, nullable: false),
                to_state = table.Column<string>(maxLength: 64, nullable: false),
                occurred_at = table.Column<DateTime>(nullable: false),
                event_type = table.Column<string>(maxLength: 128, nullable: false),
                correlation_id = table.Column<string>(maxLength: 64, nullable: false),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_outbound_saga_transitions", x => x.id)
        );

        mb.CreateIndex(
            name: "ix_outbound_saga_transitions_order_occurred",
            table: "outbound_saga_transitions",
            columns: new[] { "order_id", "occurred_at" }
        );
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable(name: "outbound_saga_transitions");
    }
}
