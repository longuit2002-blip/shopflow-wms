---
name: outbound-api-never-booted-composition-bugs
description: Finish-line U4 (cross-role denial harness) tried to boot the Outbound.Api WAF for the first time — every WAF-based Outbound test (Outbound403Tests, PickerHappyPathTests, CrossRoleDenialTests, HandoffWorkflowTests) was Skip-marked, and the saga tests build their own MassTransit harness, so the real Outbound.Api composition root had never run. It surfaced two migration bugs (fixed) and one composition-root blocker (needs a rebuild).
metadata:
  type: bug
  date: 2026-05-27
  tags: [outbound, integration-tests, migrations, masstransit, composition-root, never-ran, finish-line]
---

# Outbound.Api has never booted — composition bugs

## Context

The U4 cross-role denial harness boots `WebApplicationFactory<Program>` over
`Outbound.Api`. That had never happened: the WAF-based Outbound tests were all
`[Fact(Skip)]` stubs, and the real-bodied saga tests (`SagaHappyPathTests` etc.)
build their OWN `AddMassTransitTestHarness` rather than the production
`AddShopFlowDefaults + AddOutboundModule` composition. So `Outbound.Api`'s
composition root + the Outbound migration chain had never been exercised
end-to-end. Booting it surfaced three issues, in order.

## 1. Migration ordering — FIXED

The Outbound migration IDs sorted:
`InitialOutboundSchema(20260513000001)` →
`AddUniqueOnSagaTransitions(20260519000002)` →
`OutboundIndexAudit(20260519000003)` →
`AddOrderTransitions(20260519100001)`.

But `AddOrderTransitions` **creates** `outbound_saga_transitions`, while
`AddUniqueOnSagaTransitions` (sorting earlier) adds a UNIQUE to it →
`42P01 relation "outbound_saga_transitions" does not exist` on apply.
`AddOrderTransitions` (Sprint-7) was authored *before* the Sprint-7.5 dependents
but got a higher timestamp. **Fix:** renamed `20260519100001` →
`20260519000001` so the table-creation sorts first (right after
InitialOutboundSchema), matching authorship chronology. Same fix class as the
Sprint-13 AddPackerRole ordering correction.

## 2. OutboundIndexAudit — wrong table names — FIXED

`OutboundIndexAudit` (Sprint-7.5, judgment-authored without Docker — same as the
StockSync index audit) created indexes `ON outbound_orders` +
`ON outbound_order_lines`. Neither table exists; InitialOutboundSchema creates
`orders` + `order_lines` (no module prefix on these). `42P01` on apply.
**Fix:** corrected to the real table + index names (`ix_orders_status_created_at`
on `orders`, `ix_order_lines_order_id` on `order_lines`).

## 3. Double AddMassTransit — composition-root blocker — DEFERRED

With the schema fixed, the WAF boot throws:

```
MassTransit.ConfigurationException : AddMassTransit() was already called and may
only be called once per container.
```

`AddShopFlowDefaults` calls `services.AddMassTransit(bus => { bus.AddConsumers(asm);
bus.AddSagaStateMachines(asm); })` — so the `FulfillmentSaga` is auto-registered
there, but with a DEFAULT (in-memory) repository. `AddOutboundModule` then calls a
SECOND `AddMassTransit(bus => bus.AddSagaStateMachine<FulfillmentSaga,...>()
.EntityFrameworkRepository(...))` to give the saga its EF repository (the
`saga_state` table). MassTransit forbids two `AddMassTransit` calls → the
Outbound.Api host can't build. StockSync.Api booted (U6) because it has no saga —
its consumers register through `AddShopFlowDefaults`'s single call.

**The fix is a composition-root rebuild, not a one-liner:** the saga's
EF-repository config + the SignalR relay consumers + the bus-level
`TenantBindingSagaFilter` wiring must all live in the SINGLE `AddMassTransit` call.
The cleanest shape is a kernel bus-configurator hook — e.g.,
`AddShopFlowDefaults(..., Action<IBusRegistrationConfigurator>? configureBus = null)`
— that `Outbound.Api`'s `Program.cs` uses to add the saga + relays, with
`AddOutboundModule` dropping its own `AddMassTransit`. (The saga tests' self-built
harness already does this in one call — it's the production composition that
never reconciled the two.) Expect further never-run wiring behind this (the
TenantBindingSagaFilter attach-to-receive-endpoints step the AddOutboundModule
comment says "lives in the Api project's Program.cs" — but Program.cs doesn't do
it).

## Status (finish-line U4)

Bugs 1–2 are fixed + committed (the Outbound migration chain now applies — proven
by the boot getting past the schema to the MassTransit error). The HandoffFixture
provisioning (catalog + registered tenant + Outbound schema; Auth schema/seed/users
NOT needed — the denial path is claim-based) is implemented. The 14 cross-role
denial bodies stay Skip-marked pending the composition rebuild (bug 3); the first
body is written + ready and becomes `[ProofFact]` once the WAF boots.

## Lesson

Same as the StockSync note: WAF-based integration tests that are uniformly
Skip-marked let the composition root rot. Outbound.Api accumulated a migration
ordering bug, two wrong-table-name index migrations, and a double-AddMassTransit
that prevents the host from building — none caught because the WAF never booted
and the saga tests bypass the production composition. A single un-skipped
WAF-boot smoke test per module API would have caught all of it at introduction.
