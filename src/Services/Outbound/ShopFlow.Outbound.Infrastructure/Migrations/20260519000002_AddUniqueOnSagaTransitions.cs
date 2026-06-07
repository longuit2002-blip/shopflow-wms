using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.Outbound.Infrastructure;

#pragma warning disable CA1707 // Identifiers should not contain underscores — EF migration class name encodes timestamp + descriptor

namespace ShopFlow.Outbound.Infrastructure.Migrations;

/// <summary>
/// Sprint-7.5 U8 — add a composite UNIQUE constraint on
/// <c>outbound_saga_transitions(order_id, occurred_at, to_state)</c>
/// named <c>uq_outbound_saga_transitions_order_occurred_state</c>.
/// Closes Sprint-7 trade-off #1 — under MassTransit at-least-once
/// redelivery the saga can re-consume the same transition event and
/// re-write an identical audit row; this UNIQUE makes that physically
/// impossible at the database tier.
/// </summary>
/// <remarks>
/// <para>Pairs with the application-tier
/// <c>SagaTransitionDuplicateInterceptor</c> which catches the residual
/// <c>23505</c> raised by this constraint and treats it as a no-op,
/// preventing the saga from entering an infinite-redelivery loop.</para>
///
/// <para><strong>Pre-check.</strong> Before adding the constraint the
/// migration scans <c>outbound_saga_transitions</c> for existing
/// duplicate triples and raises a clean Postgres exception if any are
/// found. This is defensive — Sprint-7 shipped without the UNIQUE so a
/// real production environment <em>could</em> have collected duplicates
/// already; aborting the migration with a clear message is much friendlier
/// than the index-build's stock error.</para>
///
/// <para>Carries both <see cref="MigrationAttribute"/> and
/// <see cref="DbContextAttribute"/> per AGENTS.md §3.23 — without both
/// <c>MigrateAsync()</c> is a silent no-op
/// (<c>docs/solutions/2026-05-10-ef-migration-needs-attributes.md</c>).</para>
/// </remarks>
[DbContext(typeof(OutboundDbContext))]
[Migration("20260519000002_AddUniqueOnSagaTransitions")]
public sealed partial class AddUniqueOnSagaTransitions : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        // Defensive pre-check: abort with a clear message if duplicates
        // already exist on the target columns. The DO/EXCEPTION block runs
        // as part of the migration transaction so any RAISE rolls the whole
        // migration back cleanly without leaving a half-built index.
        mb.Sql(
            @"DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM outbound_saga_transitions
        GROUP BY order_id, occurred_at, to_state
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'duplicate (order_id, occurred_at, to_state) triples exist in outbound_saga_transitions; cannot add UNIQUE constraint until duplicates are reconciled';
    END IF;
END $$;"
        );

        mb.CreateIndex(
            name: "uq_outbound_saga_transitions_order_occurred_state",
            table: "outbound_saga_transitions",
            columns: new[] { "order_id", "occurred_at", "to_state" },
            unique: true
        );
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropIndex(
            name: "uq_outbound_saga_transitions_order_occurred_state",
            table: "outbound_saga_transitions"
        );
    }
}
