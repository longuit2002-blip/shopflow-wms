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

## 3. Double AddMassTransit — composition-root blocker — RESOLVED (finish-line U4, 2026-05-29)

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

**Fix shipped (finish-line U4, 2026-05-29).** Added an
`Action<IBusRegistrationConfigurator>? ConfigureBus` property to
`ShopFlowDefaultsOptions`, invoked inside the kernel's single `AddMassTransit` (after the
assembly scan, before transport selection). `Outbound.Api/Program.cs` sets
`o.ConfigureBus = OutboundServiceCollectionExtensions.ConfigureOutboundBus`, which registers the
`FulfillmentSaga` EF repository + the two SignalR relay consumers in that ONE call;
`AddOutboundModule` dropped its second `AddMassTransit`. The kernel's `AddSagaStateMachines(asm)`
scan still discovers `FulfillmentSaga`, and MassTransit tolerates the re-registration in the hook
(the explicit `.EntityFrameworkRepository(...)` wins) — verified by the WAF booting + 14 cross-role
denial proofs going green. The hook is null-default, so every other module's bus is unchanged. The
`TenantBindingSagaFilter` attach-to-receive-endpoints step is still NOT wired — it isn't needed for
the claim-based denial proofs (auth rejects before the saga runs), but the saga drive-through
happy-path (`HandoffWorkflowTests`) needs it; see bug 6.

## 4. Outbound.Api never registered ITenantCatalog — RESOLVED (finish-line U4, 2026-05-29)

With the double-AddMassTransit gone, the next never-run layer surfaced: the host failed DI
validation with `Unable to resolve service for type 'ITenantCatalog'` while activating
`StockChangedRelayConsumer`, `SagaTransitionedRelayConsumer`, and `TenantBindingHubFilter`. Cause:
`Outbound.Api/Program.cs` never called `AddControlPlane` (which registers `ITenantCatalog`) — the
same call `StockSync.Api` makes. `TenantRoutingMiddleware` needs it too, so the app was unroutable
as well as unbuildable; nothing caught it because the host never booted. **Fix:** added the
`ShopFlow.ControlPlane.Infrastructure` project reference + `builder.Services.AddControlPlane(...)`.

## 5. CreatedAtAction(nameof(GetByIdAsync)) → POST /orders + /seed return 500 — OPEN

Once the WAF booted, `POST /api/outbound/orders` (CreateAsync) and `POST .../seed` (SeedAsync) both
return **500**: `System.InvalidOperationException: No route matches the supplied values`. Both end
with `CreatedAtAction(nameof(GetByIdAsync), new { id }, …)`, but ASP.NET strips the `Async` suffix
from action names by default (`SuppressAsyncSuffixInActionNames` is unset → the action registers as
`"GetById"`), so the Location-header link generation matches no action. A real production bug, not
just a test seam: any client creating an order hits the 500 after the row is written. It hid because
the saga/controller unit tests call the controller method directly and assert the returned
`CreatedAtActionResult` object without executing the link generation. **Not fixed in U4** (outside
the cross-role-RBAC scope; switching `CreatedAtAction` → `CreatedAtRoute` would break the
`BeOfType<CreatedAtActionResult>()` assertions in `SagaHappyPathTests`); the U4 denial proofs seed
orders via a direct DbContext write to sidestep it. Fix options when tackled: set
`SuppressAsyncSuffixInActionNames = false` in `AddShopFlowControllers` (global; also fixes any other
module with the same latent pattern) OR give the GET action a route name + switch the two callers to
`CreatedAtRoute` (localized; update the affected `CreatedAtActionResult` assertions).

## 6. Saga drive-through (TenantBindingSagaFilter wiring) — OPEN

The cross-role denial proofs are claim-based: the `[Authorize(Policy)]` filter rejects before the
controller/saga runs, so they boot the WAF and assert 403 without the saga executing. The happy-path
`HandoffWorkflowTests` (Picker → Packer → Dispatcher driving one saga to Shipped) DOES need the saga
to transition through states in MassTransit consume scopes, where the per-message tenant binding
(`TenantBindingSagaFilter`, K12) must run so the saga's `OutboundDbContext` resolves the right
per-tenant connection string. That filter is registered (Scoped) but never attached to the receive
endpoints in production (`AddOutboundModule`'s comment claims Program.cs does it; it doesn't). Wiring
it + seeding `saga_state` is the remaining work to un-stub `HandoffWorkflowTests`.

## Status (finish-line U4)

Bugs 1–4 are FIXED. The Outbound.Api WAF now boots for the first time, and all **14 cross-role
denial proofs (`CrossRoleDenialTests`, AE4) pass** under `task proofs` — gated by `[ProofFact]`,
class-tagged `Category=Proof`. The HandoffFixture provisioning (catalog + registered tenant +
Outbound schema; Auth schema/seed/users NOT needed — the denial path is claim-based) is implemented;
the denial bodies seed orders via a direct DbContext write (bug 5 makes the HTTP seed endpoint 500).
Verification: `dotnet build` 0/0; unit suite 841 passing (no regression from the kernel hook);
`task proofs` → Outbound 14/14, SharedKernel-routing 5/5, ledger-property 5/5, StockSync
noisy-neighbor 1/1. Bug 5 (CreatedAtAction) + bug 6 (saga drive-through wiring) remain OPEN — both
outside the cross-role-RBAC scope this unit delivered.

## Lesson

Same as the StockSync note: WAF-based integration tests that are uniformly
Skip-marked let the composition root rot. Outbound.Api accumulated a migration
ordering bug, two wrong-table-name index migrations, and a double-AddMassTransit
that prevents the host from building — none caught because the WAF never booted
and the saga tests bypass the production composition. A single un-skipped
WAF-boot smoke test per module API would have caught all of it at introduction.
