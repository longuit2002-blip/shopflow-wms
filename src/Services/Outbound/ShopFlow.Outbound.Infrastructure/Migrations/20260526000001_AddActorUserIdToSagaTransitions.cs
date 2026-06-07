using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.Outbound.Infrastructure;

#pragma warning disable CA1707 // Identifiers should not contain underscores — EF migration class name encodes timestamp + descriptor

namespace ShopFlow.Outbound.Infrastructure.Migrations;

/// <summary>
/// Sprint-12.5 U2 — adds nullable <c>actor_user_id</c> column to
/// <c>outbound_saga_transitions</c>. Captures the operator (JWT subject)
/// who triggered an operator-initiated saga transition; NULL for
/// system-triggered chains (StockReservedV1, StockReleasedV1 counter-drain,
/// etc.). Per KTD3 the actor flows through the saga event payload to
/// <see cref="ShopFlow.Outbound.Application.Sagas.SagaTransitionObserver"/>
/// at the audit-row write site.
/// </summary>
/// <remarks>
/// <para>Carries both <see cref="MigrationAttribute"/> and
/// <see cref="DbContextAttribute"/> per AGENTS.md §3.23 — without both
/// <c>MigrateAsync()</c> is a silent no-op
/// (<c>docs/solutions/2026-05-10-ef-migration-needs-attributes.md</c>).</para>
///
/// <para>Additive nullable column — historical rows backfill to NULL.
/// No new index at Sprint-12.5 scope; future operator-attribution
/// reporting may want a partial index <c>WHERE actor_user_id IS NOT NULL</c>.
/// The Sprint-7.5 UNIQUE constraint
/// <c>uq_outbound_saga_transitions_order_occurred_state</c> is on
/// <c>(order_id, occurred_at, to_state)</c> and is unaffected.</para>
/// </remarks>
[DbContext(typeof(OutboundDbContext))]
[Migration("20260526000001_AddActorUserIdToSagaTransitions")]
public sealed partial class AddActorUserIdToSagaTransitions : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.AddColumn<Guid>(
            name: "actor_user_id",
            table: "outbound_saga_transitions",
            nullable: true
        );
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropColumn(name: "actor_user_id", table: "outbound_saga_transitions");
    }
}
