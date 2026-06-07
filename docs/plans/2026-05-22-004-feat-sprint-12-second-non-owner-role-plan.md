---
title: "feat: Sprint-12 — Second non-Owner role (Dispatcher) + 3-role hand-off proof"
type: feat
created: 2026-05-22
status: active
origin: docs/brainstorms/2026-05-22-sprint-12-second-non-owner-role-requirements.md
cut_from: v0.15.0-sprint-11
target_tag: v0.16.0-sprint-12
branch: feat/sprint-12-second-non-owner-role
---

# Sprint-12 — Second non-Owner role (Dispatcher) + 3-role hand-off proof

## Summary

Ship the Dispatcher role end-to-end. Add a 3-key Dispatcher baseline (`outbound.orders.read` + `outbound.orders.ship-confirm` + `hub.connect`) to `RolePermissionsSeed`. Wire one new UI button (ConfirmShip) on the order-detail surface with `usePerm('outbound.orders.ship-confirm')` reactive gating. Prove the stack via a Docker-backed 3-role hand-off E2E test on one order (`Picker confirms pick → Owner confirms pack → Dispatcher confirms ship`) plus a Docker-backed cross-role denial test exercising 4 negative paths. Annotated tag `v0.16.0-sprint-12` on `feat/sprint-12-second-non-owner-role`, cut from `v0.15.0-sprint-11`.

---

## Problem Frame

See origin: `docs/brainstorms/2026-05-22-sprint-12-second-non-owner-role-requirements.md` "Problem frame" section.

Sprint-11 proved the defense-in-depth stack works under 1 narrowed non-Owner role. Two failure surfaces remain unproved: **role-confusion bugs** (current 403 tests narrow Owner; no test fires a real Picker JWT against a Dispatcher endpoint) and **saga-state-ownership across role hand-off** (current Picker E2E drives 1 saga through 1 transition under 1 JWT). Sprint-12 closes both.

---

## Requirements Trace

| Origin ID | Plan owner | Notes |
|---|---|---|
| **A1** Owner | U-baseline (Sprint-9 / Sprint-11) | No changes |
| **A2** Picker | U-baseline (Sprint-11 U1) | No changes |
| **A3** Dispatcher (new) | U1, U2, U3, U4, U5 | New |
| **F1** Pick → Pack → Ship hand-off | U4 (E2E) | 3-role flow on one saga |
| **F2** Dispatcher confirms ship (UI) | U2, U3 | Gates on `Order.status === 'AwaitingShip'` (Order aggregate field, NOT saga `currentSagaState` — see KTD2) |
| **F3** Cross-role denial | U5 | 4 denial paths |
| **AE1** Fresh-tenant 31-row provision | U1 | 24 Owner + 4 Picker + 3 Dispatcher |
| **AE2** 3-role hand-off transitions → `Shipped` ≤ 30s | U4 | 10s/transition × 3 = 30s budget; saga states polled: `Picked → Packed → Shipped` (saga has no `AwaitingShip` state — KTD2) |
| **AE3** Picker → ship-confirm → 403 | U5 | |
| **AE4** Dispatcher → pick-confirm → 403 | U5 | |
| **AE5** Per-component visibility (4 sessions × `AwaitingShip`) | U3 | Vitest |
| **AE6** Additive-only re-seed (KTD1) | U1 integration test | Owner manual grant on Picker preserved |
| **AE7** `DispatcherBaseline` pinned in unit tests | U1 | |
| **R1**–**R11** | All units | See per-unit Requirements field |

---

## Key Technical Decisions

### KTD1 — Additive-only re-seed contract inherited from Sprint-11

Sprint-12's `RolePermissionsSeed` extension uses the same `ON CONFLICT (role, permission_key) DO NOTHING` mechanic Sprint-11 U1 established. Owner additions to either Picker or Dispatcher are preserved across re-seed; Owner *deletions* from the baseline REVERT on the next provision run. Documented as load-bearing in Auth AGENTS.md (Sprint-11 already states this; Sprint-12 R11 extends the line to cover Dispatcher).

**Operator-runbook step (CHANGELOG callout):** "Audit `/admin/role-permissions` before deploying Sprint-12 if any non-Owner role currently holds `outbound.orders.ship-confirm`. KTD1 will leave the existing grant in place; Sprint-12 will additionally seed Dispatcher with the same key. Two roles will hold ship-confirm post-provision, which is documented behavior but may not be the operator's intent."

### KTD2 — UI gate reads `Order.Status`, not saga `CurrentState`

The brainstorm said the ConfirmShip button appears when "order state is `Packed`". Verification against `OrdersController.PackConfirmAsync` (Sprint-3-redux) shows the handler chains `order.MarkAwaitingShip()` inside the same SaveChanges (`OrdersController.cs:841`). This moves the **Order aggregate's** `Status` column `Packed → AwaitingShip`. However the **saga's** `CurrentState` (which `OrderDetailDto.CurrentSagaState` is sourced from via `OrderRepository.GetCurrentSagaStateAsync`) only goes `Picked → Packed → Shipped` — the `AwaitingShip` State is declared in `FulfillmentSaga` but has no `During()` block, so the saga's `CurrentState` never equals `'AwaitingShip'` on the happy path (verified: `FulfillmentSaga.cs:198-220` plus the TODO at line 213 documents the missing auto-transition).

Doc-review caught this conflation pre-code (cross-persona agreement adversarial + feasibility confidence 100). The fix: **gate U3 on `detail.status === 'AwaitingShip'`** — the Order's `status` field IS exposed on `OrderDetailDto` (`web/src/api/orders.ts:88`) and DOES reach `'AwaitingShip'` in production via `MarkAwaitingShip()`. The `confirm-ship` controller pre-condition (`order.Status != OrderStatus.AwaitingShip` rejects with `order.invalid_state`) is the same field, so the UI gate mirrors the server gate's source-of-truth column exactly. The brainstorm's AE5 / F2 intent is honored.

Sprint-11's gating-on-`currentSagaState` pattern doesn't propagate to Sprint-12 because the saga genuinely had `AwaitingPick` for the pick-confirm transition; it does NOT have `AwaitingShip` for the ship-confirm transition. The pattern divergence is documented here so future role-action gates default to checking which field actually carries the user-visible pre-state, not assuming `currentSagaState` always reflects it.

### KTD3 — Reuse Sprint-11's `createIdempotentMutation` factor as-is

The factor at `web/src/hooks/useOrderMutations.ts:92-130` accepts `<TReq, TRes>` generics and a per-mutation invalidate-keys list. `confirmShip` becomes the 4th consumer with zero changes to the factor itself. Sprint-7 `seedOrder` + Sprint-11 `confirmPick` + Sprint-11 `markPickFailed` behavior preserved unchanged.

### KTD4 — Parallel `HandoffFixture` instead of generalizing Sprint-11's `PickerFixture`

Sprint-11 just shipped + the CI Docker tier on `PickerFixture` is green. Generalizing it into a shared multi-role fixture would require touching Sprint-11's fixture file and re-running Sprint-11's CI tier. Lower regression risk to ship a parallel `tests/ShopFlow.Outbound.IntegrationTests/Handoff/HandoffFixture.cs` that mirrors `PickerFixture`'s shape but seeds 3 users (Picker + Owner + Dispatcher) and exposes 3 `JwtBuilder.Build(...)` outputs. ~85% of the fixture body is reused conceptually; a future Sprint-12.5 or Sprint-13 refactor can consolidate if duplication proves painful.

### KTD5 — Zero-flake `MockShippingProvider` registered in `HandoffFixture`

`OrdersController.ConfirmShipAsync` calls `IMockShippingProvider.CreateLabelAsync` with the production Polly retry pipeline. Without a zero-flake configured instance the E2E test is non-deterministic on CI Docker tier. Plan registers a factory-form override in `HandoffFixture.InitializeAsync` via `b.ConfigureTestServices(...)`:

```csharp
b.ConfigureTestServices(s =>
{
    s.RemoveAll<IMockShippingProvider>();
    s.AddSingleton<IMockShippingProvider>(sp =>
        MockShippingProvider.WithFlakeRate(
            sp.GetRequiredService<ResiliencePipeline>(),
            0.0));
});
```

Factory-form (not instance-form) because the `ResiliencePipeline` is registered by `OutboundServiceCollectionExtensions.AddOutboundModule` and must be resolved from the test service provider, not constructed in `InitializeAsync` where the pipeline builder isn't in scope. `RemoveAll<IMockShippingProvider>()` displaces the production registration; `AddSingleton(factory)` re-registers with the zero-flake instance. WAF semantics guarantee `ConfigureTestServices` runs after `ConfigureServices`, so the replacement takes effect before any controller resolves the interface.

**Carrying cost (adversarial-F6):** the zero-flake override means CI never exercises the Polly retry path through the HTTP layer. Existing `MockShippingProviderTests` (unit-scope) cover retry behavior at lower fidelity. If carrier-flake regression coverage gaps materialize in production, a Sprint-12.5 follow-up adds one fact with `FlakeRate=0.99 + retry-attempts=4` to force a deterministic success-on-retry. Listed in "Deferred to Follow-Up Work".

**Fallback if the WAF singleton replacement doesn't propagate (R-8):** the original brainstorm hinted at `b.UseSetting("Shipping:FlakeRate", "0.0")` — but no such config key exists today (`MockShippingProvider` reads flake-rate via constructor argument, not `IConfiguration`). The fallback requires constructor refactoring on `MockShippingProvider` to read `Shipping:FlakeRate` from `IOptions<>` — kept in mind but not landed in Sprint-12.

### KTD6 — Test namespace `.Handoff` (no domain collision risk)

Sprint-11 hit `CS0234` from `Picker` colliding with `ShopFlow.Outbound.Domain.Picker` and pivoted to namespace `.PickerE2E`. `Handoff` doesn't shadow any existing Outbound domain type (verified via grep — no `Handoff` type in `ShopFlow.Outbound.Domain`). Namespace stays `ShopFlow.Outbound.IntegrationTests.Handoff`; directory `Handoff/`.

### KTD7 — 30-second baked-in timeout for the 3-transition hand-off

Sprint-11 KTD5 was a 10s timeout for one transition. The hand-off test fires 3 transitions sequentially; each transition has its own poll loop with a 10s budget (500ms interval × 20 attempts). Total wall-time budget 30s for the happy path. If CI proves flaky beyond 1-in-20, raise per-transition timeout to 15s as a deviation rather than re-architecting.

### KTD8 — Version bump `v0.16.0-sprint-12` (minor)

Net-new feature surface (third role, hand-off proof, ConfirmShip UI). Matches Sprint-9.5 + Sprint-11 precedent for feature-bearing sprints. Patch bump would understate the user-visible capability addition.

### KTD9 — No `dispatcherBaseline.ts` ↔ backend `DispatcherBaseline` contract test

Sprint-10.5 U2 `AdminTsCatalogContractTests` exists for `web/src/api/admin.ts` ↔ backend `PermissionKeys.All` — the source-of-truth catalog. The Sprint-12 `dispatcherBaseline.ts` is a derivative (3 strings copied from `PermissionKeys.All` constants) and is consumed by per-component Vitest tests that would catch any string-level drift quickly. Adding a third reflection test class adds carrying cost without proportional value. Deferred; revisit if drift surfaces in practice.

### KTD10 — `ConfirmShipResponse` shape differs from `OrderResponse`

`confirm-ship` returns `ConfirmShipResponse(LabelUrl, TrackingNumber, OrderResponse Order)` — not just `OrderResponse` like `confirm-pick`. The `confirmShip` mutation's TypeScript type is a new `ConfirmShipResponse` interface mirroring the backend record. `useOrderMutations.confirmShip` consumes the factor with `<{orderId: string}, ConfirmShipResponse>` generics. The success toast surfaces `tracking_number` via `successBody` (4-second dwell).

**Persistent surface (design-F2 doc-review decision):** `OrderDetailDto` already carries nullable `labelUrl` + `trackingNumber` fields (verified at `web/src/api/orders.ts:88`), and the post-confirm query invalidation re-fetches the detail. Sprint-12 accepts toast-as-primary-surface; the order-detail header surfaces `trackingNumber` post-ship on re-render as a secondary persistent fallback IF the field is non-null on the refreshed detail. Implementation: when `detail.trackingNumber !== null && detail.labelUrl !== null`, render a small tracking-number pill in the existing header `<div data-testid="order-detail-tracking">` between the saga state pill and channel label. If operations report toast-miss-then-lookup-pain post-Sprint-12, a Sprint-12.5 polish lands a richer tracking-info surface. The pill is a low-cost mitigation that doesn't require new DTO fields or query keys.

---

## Implementation Units

### U1. Backend — `RolePermissionsSeed` Dispatcher baseline extension

**Goal:** Extend Sprint-11's `RolePermissionsSeed` with a `DispatcherBaseline` static readonly list (3 keys), reusing the shared `InsertAsync` helper. Idempotent re-seed contract (KTD1) preserved.

**Requirements:** R1, R2, R10, AE1, AE6, AE7. Origin actors A3.

**Dependencies:** None (Sprint-11 U1 patterns inherited).

**Files:**
- Modify: `tools/shopflow-migrate/Provisioning/RolePermissionsSeed.cs`
- Modify: `tests/ShopFlow.Migrate.UnitTests/Provisioning/RolePermissionsSeedTests.cs`
- Modify: `tests/ShopFlow.Migrate.IntegrationTests/Provisioning/RolePermissionsSeedIntegrationTests.cs`

**Approach:**

The Sprint-11 file already exposes the `InsertAsync` shared helper consumed by both Owner + Picker loops. Add a third `DispatcherBaseline` static readonly list referencing 3 `PermissionKeys` constants:
- `PermissionKeys.OutboundOrdersRead`
- `PermissionKeys.OutboundOrdersShipConfirm`
- `PermissionKeys.HubConnect`

Extend the `SeedAsync` method body to also iterate `DispatcherBaseline` and call `InsertAsync(conn, tx, "Dispatcher", key, ct)` for each (5-arg signature matching the existing Owner + Picker loops inside the same `BeginTransactionAsync` block at `RolePermissionsSeed.cs:64-83`). Update the class XML doc to reflect the 3-role current state ("Owner gets all 24 keys via `PermissionKeys.All`; Picker gets 4-key baseline; Dispatcher gets 3-key baseline; both Picker and Dispatcher use additive-only re-seed semantics per KTD1").

**Patterns to follow:**
- Sprint-11 U1 `PickerBaseline` declaration shape — same `static readonly List<string>` initialization with `PermissionKeys.*` constants
- Sprint-11 U1 shared `InsertAsync` call site — call once per Dispatcher key inside the same `SeedAsync` method body, no new transaction
- Sprint-11 U1 XML doc shape — class-level summary states the additive-only re-seed contract

**Test scenarios:**
- **Covers AE7.** `DispatcherBaseline_HasExactly3Keys` — reflection assertion against the public surface of `RolePermissionsSeed.DispatcherBaseline.Count == 3`.
- `DispatcherBaseline_ContainsOutboundOrdersRead` — string equality against `PermissionKeys.OutboundOrdersRead`.
- `DispatcherBaseline_ContainsOutboundOrdersShipConfirm` — string equality against `PermissionKeys.OutboundOrdersShipConfirm`.
- `DispatcherBaseline_ContainsHubConnect` — string equality against `PermissionKeys.HubConnect`.
- `DispatcherBaseline_DoesNotContainAuthAdminKeys` — sanity: no `auth.admin.*` keys in the Dispatcher baseline (an OwnerCritical leak would be a regression).
- `DispatcherBaseline_DoesNotContainOutboundOrdersPickConfirm` — sanity: no cross-contamination with Picker's transition.
- `PickerBaseline_StillHasExactly4Keys` — regression guard on Sprint-11 baseline.
- `PickerBaseline_DoesNotContainOutboundOrdersShipConfirm` — **(security-F1 mitigation)** explicit baseline-isolation guard. Asserts the canonical Picker baseline has NO `outbound.orders.ship-confirm` so a future refactor cannot accidentally cross-grant Dispatcher's carrier-cost endpoint to Picker. The runtime additive-only contract (KTD1) preserves operator-added overlaps; this test ensures the BASELINE doesn't ship pre-overlapped.
- `OwnerBaseline_StillUsesPermissionKeysAll` — regression guard.

Integration scenario (Migrate.IntegrationTests):
- **Covers AE1.** `Provision_FreshTenant_Yields31RolePermissionsRows` — provision a fresh test DB via `MigrateTenantFixture`, count rows in `role_permissions`, assert exactly 31 (24 Owner + 4 Picker + 3 Dispatcher). Decompose by role and pin per-role counts in separate asserts so a Sprint-13 baseline change can be located precisely.
- **Covers AE6.** `Provision_Idempotent_PreservesOwnerAdditionToPicker` — extension of Sprint-11's existing additive-preservation scenario. Seed once. Owner-manually grant `outbound.orders.ship-confirm` to Picker via raw INSERT. Re-run `SeedAsync`. Assert Picker row count grew to 5 (4 baseline + 1 Owner addition) AND Dispatcher row count is 3 (Dispatcher baseline written cleanly, Picker addition preserved untouched).

**Verification:** Migrate.UnitTests passes 52 (Sprint-11 baseline) + 9 new (~61 total — added the security-F1 Picker-baseline-isolation guard). Migrate.IntegrationTests passes 4 (Sprint-11) + 2 new = 6 scenarios. `dotnet build` 0 errors + 0 warnings.

---

### U2. Frontend — `dispatcherBaseline.ts` + `useOrderMutations.confirmShip`

**Goal:** Add the `DISPATCHER_BASELINE_PERMS` constant + wire `confirmShip` as the 4th consumer of the shared `createIdempotentMutation` factor. Add `ordersApi.confirmShip` API wrapper. No UI changes in this unit.

**Requirements:** R3, R5, R10. Origin actors A3.

**Dependencies:** None (Sprint-11 U2 patterns inherited).

**Files:**
- Create: `web/src/lib/auth/dispatcherBaseline.ts`
- Modify: `web/src/api/orders.ts`
- Modify: `web/src/hooks/useOrderMutations.ts`
- Modify: `web/src/hooks/useOrderMutations.test.ts`

**Approach:**

`web/src/lib/auth/dispatcherBaseline.ts` mirrors Sprint-11's `pickerBaseline.ts` shape — a single `export const DISPATCHER_BASELINE_PERMS: readonly string[]` with the 3 canonical strings as TypeScript string literals (`'outbound.orders.read'`, `'outbound.orders.ship-confirm'`, `'hub.connect'`). The constant exists for two purposes: (a) per-component Vitest tests build narrowed `perm[]` arrays from it; (b) future Dispatcher-aware UI surfaces (e.g., admin views) read it for display logic.

`web/src/api/orders.ts` gains:
```typescript
export interface ConfirmShipResponse {
  labelUrl: string;
  trackingNumber: string;
  order: OrderResponse;
}
```
and a method on `ordersApi`:
```
confirmShip(orderId, options?: MutationOptions) → Promise<ConfirmShipResponse>
```
posting `POST /api/outbound/orders/{id}/confirm-ship` with the idempotency-key header threaded the same way `confirmPick` does it.

`web/src/hooks/useOrderMutations.ts` gains:
- A `ConfirmShipVariables` interface (just `{ orderId: string }`).
- A `useConfirmShipMutation` const built via `createIdempotentMutation<ConfirmShipVariables | string, ConfirmShipResponse>(...)`. Invalidate keys mirror `confirmPick` exactly: `[['orders'], ['order-detail'], ['order-transitions']]`. Success-toast title bilingual (`'Xác nhận giao hàng thành công'` / `'Ship confirmed'`); success-body optional, surfacing `res.trackingNumber`.
- The `useOrderMutations` aggregator hook returns a fourth field `confirmShip` alongside `seedOrder`, `confirmPick`, `markPickFailed`.

**Technical design (directional, not implementation specification):**

```ts
// useOrderMutations.ts — addition, mirrors confirmPick almost verbatim
export const useConfirmShipMutation = createIdempotentMutation<
  ConfirmShipVariables | string,
  ConfirmShipResponse
>(
  (input, key) => {
    const orderId = typeof input === 'string' ? input : input.orderId;
    return ordersApi.confirmShip(orderId, { idempotencyKey: key });
  },
  [['orders'], ['order-detail'], ['order-transitions']],
  {
    successTitle: t('Xác nhận giao hàng thành công', 'Ship confirmed'),
    successBody: (res) => res.trackingNumber, // optional UX flourish
    errorTitle: t('Lỗi xác nhận giao hàng', 'Ship confirm failed'),
  },
);
```

**Patterns to follow:**
- Sprint-11 U2 `confirmPick` consumer wiring at [useOrderMutations.ts:168-181](web/src/hooks/useOrderMutations.ts) — copy the shape, swap strings + variable type
- Sprint-11 U2 `ordersApi.confirmPick` shape at `web/src/api/orders.ts:212` — POST without body, idempotency-key threaded via `MutationOptions`
- Sprint-11 U2 aggregator hook at `useOrderMutations.ts:216-221`

**Test scenarios:**
- `useConfirmShipMutation_PostsToCorrectEndpoint` — mock `ordersApi.confirmShip`, fire the mutation, assert it received `orderId` + options containing a non-empty `idempotencyKey`.
- `useConfirmShipMutation_GeneratesFreshULIDPerCall` — fire twice, capture both keys, assert they differ + both match ULID regex.
- `useConfirmShipMutation_InvalidatesExpectedQueryKeys` — on success, assert `qc.invalidateQueries` called with `['orders']`, `['order-detail']`, `['order-transitions']`.
- `useConfirmShipMutation_PushesSuccessToast` — on success, assert toast pushed with kind `'success'` + title containing 'Ship confirmed'.
- `useConfirmShipMutation_PushesErrorToastWithTraceId` — mock `ApiError` with `traceId` in body, assert error toast carries the traceId.
- `useOrderMutations_AggregatorReturnsAllFour` — call hook, assert `result` has `seedOrder`, `confirmPick`, `markPickFailed`, AND `confirmShip` fields.

**Verification:** Vitest 474 passing baseline preserved; ~6 new `confirmShip` tests pass; `useOrderMutations.test.ts` total grows accordingly.

---

### U3. Frontend — ConfirmShip button on order-detail surface

**Goal:** Add the ConfirmShip button to `web/src/routes/_auth/orders/$orderId.tsx`, gated by `usePerm('outbound.orders.ship-confirm')` + `detail.status === 'AwaitingShip'` (Order aggregate field, NOT saga `currentSagaState` — see KTD2 doc-review correction). Per-component Vitest covers 4 visibility combinations.

**Requirements:** R4, R6, R10, AE5. Origin flow F2.

**Dependencies:** U2 (consumes `confirmShip` from the aggregator).

**Files:**
- Modify: `web/src/routes/_auth/orders/$orderId.tsx`
- Modify: `web/src/routes/_auth/orders/$orderId.test.tsx` (existing Sprint-11 test file)

**Approach:**

The order-detail route already has the pattern (Sprint-11 U2): a `<section data-testid="order-detail-pick-actions">` rendered conditionally when `canPickConfirm && currentSagaState === 'AwaitingPick' && !justConfirmed`. Sprint-12 adds a **parallel** `<section data-testid="order-detail-ship-actions">` rendered conditionally when `canShipConfirm && detail.status === 'AwaitingShip' && !justShipped`. Two sections, two `justX` local state booleans, two `usePerm` calls — symmetric to Sprint-11.

**Field difference vs Sprint-11 (KTD2):** the pick-actions section gates on `currentSagaState` (which DOES carry `AwaitingPick`). The ship-actions section gates on `status` (Order aggregate field, which carries `AwaitingShip`). Saga's `CurrentState` never equals `'AwaitingShip'` on the happy path; the Order's `Status` does. Both fields are already on `OrderDetailDto`.

Rationale for parallel sections (not a renamed combined section): the two state windows (`currentSagaState === 'AwaitingPick'` vs `status === 'AwaitingShip'`) are mutually exclusive in practice, so only one section ever renders at a time. Separate `data-testid`s preserve Sprint-11's per-component test selectors and let Sprint-12 add new tests without modifying Sprint-11's assertions.

The new section sits between the existing `order-detail-pick-actions` section and `order-detail-lines`, so the visual ordering Pipeline → PickActions → ShipActions → LineItems → TransitionsLog reads naturally as the order progresses.

Component adds at the top of the function body:
- `const canShipConfirm = usePerm('outbound.orders.ship-confirm');`
- `const { confirmShip } = useOrderMutations(); // existing destructure extended`
- `const [justShipped, setJustShipped] = useState(false);`
- `useEffect` clears `justShipped` when `detail.status !== 'AwaitingShip'` (Sprint-11 pattern adapted to the `status` field).

Button JSX mirrors `confirm-pick-button` shape with new `data-testid="confirm-ship-button"`. Bilingual label `'Xác nhận giao hàng' / 'Confirm Ship'`. No `MarkShipFailedModal` ships at Sprint-12 (origin scope boundary). **Error path behavior:** the `confirmShip` mutation's `onError` is handled by the shared `createIdempotentMutation` factor (pushes error toast with idempotency-key + traceId); `justShipped` stays `false` on error, so the button re-appears for retry. No `aria-errormessage` on the button — toast is sufficient at Sprint-12; persistent error surface is a Sprint-12.5 polish item if operations report it as a problem.

**Persistent tracking-pill (KTD10 — design-F2 mitigation):** the existing order-detail header gains a conditional `<div data-testid="order-detail-tracking">` rendered when `detail.trackingNumber !== null && detail.labelUrl !== null`. Renders the tracking number as a `<Pill kind="ok">` between the saga state pill and the channel label. Persistent post-ship fallback for Dispatchers who miss the success toast. No new DTO fields, no new query keys — relies entirely on the existing `OrderDetailDto` nullable fields and the post-confirm invalidation already wired by `useOrderMutations.confirmShip`.

**Confirmation-step design decision (design-F3):** Sprint-12 chooses single-click-fires (no Modal interstitial) for ConfirmShip, asymmetric with Sprint-11's MarkPickFailed Modal pattern. Rationale: ConfirmShip is operator-initiated on an order the operator has already verified packed (the saga's `AwaitingShip` gate ensures this); the carrier-call cost is the standard happy path, not an exception. MarkPickFailed needed a reason capture (the operator inputs free-text justification), so a Modal is the right primitive. ConfirmShip has no operator input to capture, so a single-click fire matches the existing ConfirmPick pattern. If post-launch operator feedback surfaces accidental clicks as a real problem, a Sprint-12.5 polish lands a confirmation step.

**Technical design (directional, not implementation specification):**

```tsx
{canShipConfirm
  && detail.status === 'AwaitingShip'
  && !justShipped && (
  <section
    data-testid="order-detail-ship-actions"
    aria-label={t('Tác vụ Dispatcher', 'Dispatcher actions')}
    style={{ display: 'flex', gap: 'var(--s-2)', flexWrap: 'wrap' }}
  >
    <button
      type="button"
      className="btn primary"
      data-testid="confirm-ship-button"
      disabled={confirmShip.isPending}
      aria-busy={confirmShip.isPending ? true : undefined}
      onClick={() =>
        confirmShip.mutate(orderId, {
          onSuccess: () => setJustShipped(true),
        })
      }
    >
      {confirmShip.isPending
        ? t('Đang xác nhận…', 'Confirming…')
        : t('Xác nhận giao hàng', 'Confirm Ship')}
    </button>
  </section>
)}
```

**Patterns to follow:**
- Sprint-11 U2 `order-detail-pick-actions` section at [`web/src/routes/_auth/orders/$orderId.tsx:240-281`](web/src/routes/_auth/orders/$orderId.tsx#L240) — copy the conditional shape, swap state name + perm key + buttons
- Sprint-11 U2 `justConfirmed` optimistic-hide via local state + `useEffect` clear-on-state-change at lines 95-109
- Sprint-11 U2 `usePerm` reactive gate (KTD3 corrected) — do NOT use `hasPerm` snapshot

**Test scenarios:**
- **Covers AE5.** `OrderDetail_DispatcherSession_AwaitingShip_ShowsConfirmShipButton` — render route with mocked `useAuth` returning Dispatcher `perm[]` + mocked `useOrderDetailQuery` returning `status: 'AwaitingShip'`. Assert `getByTestId('confirm-ship-button')` is in the document.
- **Covers AE5.** `OrderDetail_PickerSession_AwaitingShip_HidesConfirmShipButton` — Picker `perm[]` (lacks `ship-confirm`) + `status: 'AwaitingShip'`. Assert `queryByTestId('confirm-ship-button')` is null.
- **Covers AE5.** `OrderDetail_OwnerSession_AwaitingShip_ShowsConfirmShipButton` — Owner `perm[]` (has all 24 keys including ship-confirm) + `status: 'AwaitingShip'`. Assert button visible.
- **Covers AE5.** `OrderDetail_DispatcherSession_AwaitingPickStatus_HidesConfirmShipButton` — Dispatcher `perm[]` but order at `status: 'AwaitingPick'`. Assert button hidden (state gate fails even when perm is sufficient).
- `OrderDetail_DispatcherSession_PackedStatus_HidesConfirmShipButton` — `status: 'Packed'` (transient state between pack-confirm save + chained `MarkAwaitingShip`; in practice arrives in same SaveChanges so rare to observe, but exercise the gate). Hidden.
- `OrderDetail_DispatcherSession_ShippedStatus_HidesConfirmShipButton` — terminal `status: 'Shipped'`. Hidden.
- `OrderDetail_ConfirmShipClick_FiresMutation` — render with `status: 'AwaitingShip'`, click button, assert `confirmShip.mutate` called with the orderId.
- `OrderDetail_PostConfirm_OptimisticHide` — fire onSuccess, assert `justShipped` truthy + button hidden via test ID query.
- `OrderDetail_ConfirmShipError_ButtonRemainsVisibleForRetry` — fire onError on the mutation, assert `justShipped` stays false AND `getByTestId('confirm-ship-button')` still in document. Pins the documented error-recovery behavior.
- `OrderDetail_ShipActions_PassesAxeSmokeTest` — wrap the rendered ship-actions section in `axe(section)` assertion mirroring Sprint-11 U2's pick-actions a11y test pattern. Zero violations expected. Closes the doc-review design-F4 regression-against-Sprint-11-baseline finding.
- **(design-F2 mitigation)** `OrderDetail_ShippedOrder_RendersTrackingPill` — render route with `status: 'Shipped'` + `trackingNumber: 'SHIP-12345'` + `labelUrl: 'https://carrier.test/label/12345'`. Assert `getByTestId('order-detail-tracking')` is in the document AND contains `'SHIP-12345'`. Pins the persistent fallback for Dispatchers who miss the success toast.
- `OrderDetail_AwaitingShipOrder_NoTrackingPill` — pre-ship state with `trackingNumber: null`. Assert `queryByTestId('order-detail-tracking')` is null. Pins the conditional render gate.

**Verification:** Vitest 474 Sprint-11 baseline + ~8 new ship-action visibility scenarios + ~2 interaction scenarios + 1 axe smoke + 2 tracking-pill scenarios = ~13 new tests pass. All 4 origin AE5 conditions covered. Sprint-11's per-section axe-test baseline preserved.

---

### U4. Backend — 3-role hand-off E2E test + `HandoffFixture`

**Goal:** Ship a parallel `HandoffFixture` (mirrors `PickerFixture` shape, seeds 3 users, exposes 3 JWT builders) + `HandoffWorkflowTests` that drives one order from `AwaitingPick` to `Shipped` via 3 different role JWTs. Skip-marked locally; CI runs full Docker tier.

**Requirements:** R7, R10, AE2. Origin flow F1.

**Execution note:** Test-first cadence. Write the `HandoffWorkflowTests.HappyPath_AllThreeRolesDriveSagaToShipped` body as a failing test (asserting the 3 HTTP 200s + final state) BEFORE the fixture wires the third (Dispatcher) user. The fixture exists for Picker (Sprint-11 precedent); failing the test against an under-provisioned fixture forces the seed step + JWT builder for Dispatcher to land correctly.

**Dependencies:** U1 (Dispatcher baseline must be seedable by the fixture's `RolePermissionsSeed.SeedAsync` call).

**Files:**
- Create: `tests/ShopFlow.Outbound.IntegrationTests/Handoff/HandoffFixture.cs`
- Create: `tests/ShopFlow.Outbound.IntegrationTests/Handoff/HandoffWorkflowTests.cs`

**Approach:**

`HandoffFixture` is structurally `PickerFixture.cs` with these deltas:
1. Three user IDs: `OwnerUserId`, `PickerUserId`, `DispatcherUserId`; three emails (`owner@`/`picker@`/`dispatcher@<tenant>.test`).
2. Three seed steps in `InitializeAsync` after the Sprint-11 `RolePermissionsSeed` call (which now writes Owner + Picker + Dispatcher baselines per U1). Each step is a raw `INSERT INTO users (...)` with the appropriate `role` value.
3. Three convenience JWT-builder accessors: `BuildOwnerJwt()`, `BuildPickerJwt()`, `BuildDispatcherJwt()`. Each calls `JwtBuilder.Build(...)` with the appropriate user + role + role-baseline `perm[]` list. Use `DispatcherBaseline` from the U1 source list directly so a Sprint-13 key change in `RolePermissionsSeed.DispatcherBaseline` propagates automatically.
4. **`MockShippingProvider` override (KTD5):** `b.ConfigureTestServices(s => { s.RemoveAll<IMockShippingProvider>(); s.AddSingleton<IMockShippingProvider>(MockShippingProvider.WithFlakeRate(pipeline, 0.0)); })` — zero-flake instance ensures `ConfirmShipAsync` deterministically succeeds on first attempt.
5. New collection: `[CollectionDefinition("Handoff")]` + `HandoffCollection : ICollectionFixture<HandoffFixture>`. Test class references the collection by name. Picker collection from Sprint-11 stays untouched.
6. **MT bus readiness wait (adversarial-F4 doc-review mitigation):** After `WebApplicationFactory<Program>` constructs the host, resolve `IBusControl` from `Factory.Services` and `await busControl.WaitUntilStarted(...)` before exposing the fixture as ready. Without this, the first `confirm-pick` POST can publish to a queue with no attached consumer, the saga never advances, and the 10s poll times out on a startup race rather than a real defect. Wall-time of each `PollUntilSagaState` call logged via `ITestOutputHelper` so CI flake investigations have observable evidence.

The test class has one happy-path scenario and is Skip-marked at the class level with the same Sprint-11 message structure ("Sprint-12 U4: Docker-backed fixture wired in CI tier; dev machine has no Docker daemon").

**Technical design — happy-path flow (directional):**

```
[Fact(Skip = "Sprint-12 U4: Docker-backed fixture; CI Docker tier only")]
public async Task HappyPath_AllThreeRolesDriveSagaToShipped()
{
    // 1. Seed an order directly into `orders` table with Status = AwaitingPick
    //    AND insert matching `saga_state` row with CurrentState = "AwaitingPick".
    //    Mirror Sprint-11 PickerHappyPathTests' direct-DbContext seed pattern.
    var orderId = await SeedOrderInAwaitingPickAsync(fixture);

    // 2. Picker confirms pick.
    var pickerJwt = fixture.BuildPickerJwt();
    var pickResp = await PostConfirmPickAsync(fixture, orderId, pickerJwt);
    Assert.Equal(HttpStatusCode.OK, pickResp.StatusCode);
    await PollUntilSagaState(fixture, orderId, "Picked", TimeSpan.FromSeconds(10));

    // 3. Owner confirms pack. PackConfirmAsync chains `order.MarkAwaitingShip()`
    //    in the same SaveChanges (OrdersController.cs:841) — Order.Status moves
    //    Packed → AwaitingShip. The saga itself transitions Picked → Packed and
    //    has NO AwaitingShip state on the happy path (saga's `Packed → Shipped`
    //    is direct via the ShipConfirmed handler — FulfillmentSaga.cs:198-220,
    //    TODO at line 213 documents the missing auto-transition). Poll the
    //    saga's CurrentState for "Packed", AND separately verify the Order's
    //    Status reached "AwaitingShip" via the GET /orders/{id} response.
    var ownerJwt = fixture.BuildOwnerJwt();
    var packResp = await PostConfirmPackAsync(fixture, orderId, ownerJwt, weight: 1500m);
    Assert.Equal(HttpStatusCode.OK, packResp.StatusCode);
    await PollUntilSagaState(fixture, orderId, "Packed", TimeSpan.FromSeconds(10));
    var midDetail = await GetOrderDetailAsync(fixture, orderId, ownerJwt);
    Assert.Equal("AwaitingShip", midDetail.status); // Order aggregate field

    // 4. Dispatcher confirms ship. Zero-flake mock provider (KTD5) returns
    //    a deterministic label on first attempt.
    var dispatcherJwt = fixture.BuildDispatcherJwt();
    var shipResp = await PostConfirmShipAsync(fixture, orderId, dispatcherJwt);
    Assert.Equal(HttpStatusCode.OK, shipResp.StatusCode);
    await PollUntilSagaState(fixture, orderId, "Shipped", TimeSpan.FromSeconds(10));

    // 5. Final GET /orders/{id} returns currentSagaState == "Shipped".
    var detail = await GetOrderDetailAsync(fixture, orderId, ownerJwt);
    Assert.Equal("Shipped", detail.currentSagaState);
}
```

Polling helper `PollUntilSagaState` mirrors Sprint-11 U3's `PickerHappyPathTests.PollUntilSagaStateAsync` shape — 500ms interval, configurable timeout.

**Patterns to follow:**
- Sprint-11 U3 `PickerFixture.cs` structure — `IAsyncLifetime` + Testcontainers Postgres + `WebApplicationFactory<Program>` + cross-module DB schema apply
- Sprint-11 U3 `PickerHappyPathTests.cs` saga seed pattern — direct DbContext writes to BOTH `orders.Status` AND `saga_state.CurrentState`
- Sprint-10.5 U4 `NarrowedJwtBuilder` MSBuild Compile-link pattern (Sprint-11 KTD4 inherited)
- Sprint-3-redux `MockShippingProvider.WithFlakeRate` factory

**Test scenarios (one fact, multiple assertion clauses):**
- **Covers AE2.** `HappyPath_AllThreeRolesDriveSagaToShipped` — full 3-role flow as designed above. Asserts each transition returns 200, each poll converges within 10s per transition, final state is `Shipped`. Total wall-time budget 30s (KTD7).
- *(Optional)* `HappyPath_TransitionRowsRecordedWithCorrectEventTypes` — secondary fact querying `outbound_saga_transitions` table post-flow, asserting 3 rows with `to_state in ('Picked', 'Packed', 'Shipped')`. Defer if it adds material complexity to the fixture; mention as a Sprint-12.5 candidate.

**Verification:** Test class compiles. Skip-marked locally → 0 failures locally. CI Docker tier (chaos-nightly + per-PR integration workflow) runs the unskipped variant in ≤ 60s wall-time per execution.

---

### U5. Backend — Cross-role denial tests (4 negative paths)

**Goal:** Pin the 4 cross-role denial paths via Docker-backed tests reusing `HandoffFixture`. Each fact issues a real non-Owner JWT (not narrowed Owner) and asserts HTTP 403 + saga state unchanged.

**Requirements:** R8, R10, AE3, AE4. Origin flow F3.

**Dependencies:** U4 (consumes `HandoffFixture`).

**Files:**
- Create: `tests/ShopFlow.Outbound.IntegrationTests/Handoff/CrossRoleDenialTests.cs`

**Approach:**

One test class in the `Handoff` collection, four `[Fact(Skip = "Sprint-12 U5: Docker-backed CI tier")]` methods. Each:
1. Seeds an order into the appropriate pre-state (AwaitingPick for pick attempts; AwaitingShip for ship attempts; Picked for pack attempts).
2. Issues the wrong-role JWT.
3. Asserts HTTP 403 with `errorCode == "auth.forbidden"` in the ProblemDetails body.
4. Asserts the saga state did NOT advance — query `saga_state.CurrentState` post-attempt, assert it matches the pre-attempt state.

The pre-state seeding for each is direct DbContext writes (Sprint-11 U3 pattern), keeping the negative-path tests fast (no need to drive prior transitions).

**Test scenarios:**
- **Covers AE3.** `Picker_AttemptsShipConfirm_Returns403_AndSagaUnchanged` — seed order with `orders.Status = "AwaitingShip"` + `saga_state.CurrentState = "Packed"` (saga reality per KTD2), fire `POST /confirm-ship` with Picker JWT, assert 403 + ProblemDetails `errorCode == "auth.forbidden"` + `orders.Status` still `AwaitingShip` + `saga_state.CurrentState` still `Packed`.
- `Picker_AttemptsPackConfirm_Returns403_AndSagaUnchanged` — seed at `orders.Status = "Picked"` + `saga_state.CurrentState = "Picked"`, Picker JWT against `/confirm-pack`, same assertions for both fields-unchanged.
- **Covers AE4.** `Dispatcher_AttemptsPickConfirm_Returns403_AndSagaUnchanged` — seed at `orders.Status = "AwaitingPick"` + `saga_state.CurrentState = "AwaitingPick"`, Dispatcher JWT against `/confirm-pick`, 403 + state-still-AwaitingPick.
- `Dispatcher_AttemptsPackConfirm_Returns403_AndSagaUnchanged` — seed at `orders.Status = "Picked"` + `saga_state.CurrentState = "Picked"`, Dispatcher JWT against `/confirm-pack`, 403 + state-still-Picked.
- **(adversarial-F3 mitigation)** `Dispatcher_AttemptsPickConfirm_OnAwaitingShipOrder_Returns403_NotStateError` — seed at `orders.Status = "AwaitingShip"` (wrong state for pick-confirm) + Dispatcher JWT (wrong role for pick-confirm). Assert response is HTTP 403 with `errorCode == "auth.forbidden"`, NOT HTTP 400 with `errorCode == "order.invalid_state"`. Proves the `[Authorize(Policy)]` filter executes BEFORE the controller's pre-state check at `OrdersController.cs:895` — a middleware-ordering regression that swapped the response code would leak the order's state to an unauthorized caller. Closes the Sprint-10 ordering-regression class the original 4 facts don't distinguish.
- **(adversarial-F8 mitigation)** `PickerWithManualShipConfirmGrant_CanShip_BehavioralPin` — mint a Picker JWT that ADDITIONALLY carries `outbound.orders.ship-confirm` in `perm[]` (simulating the AE6 operator-pre-grant case). Seed order at `orders.Status = "AwaitingShip"`. Fire `POST /confirm-ship` with this augmented JWT. Assert HTTP 200 + saga reaches `Shipped`. Then separately assert the same JWT against `/confirm-pack` returns 403 (Picker still doesn't have `pack-confirm`). Pins the KTD1 additive-only contract's behavioral consequence: an operator who grants Picker `ship-confirm` HAS granted ship capability — there is no defense-in-depth surprise rescue. The operator-runbook callout is the only mitigation; this test ensures the documented behavior matches reality.

**Verification:** 6 facts compile + skip-marked locally. CI Docker tier executes all 6 and reports green.

---

### U6. Sign-off + docs + tag

**Goal:** Sprint-12 sign-off mirroring Sprint-11 shape. Update Auth AGENTS.md, README current-stage, CLAUDE.md current-stage, CHANGELOG entry. Annotated tag `v0.16.0-sprint-12` + push branch + tag to origin per standing user preference.

**Requirements:** R9, R11. All AEs verified via earlier units.

**Dependencies:** U1 through U5 all merged + CI green.

**Files:**
- Create: `docs/phase-gates/2026-05-22-sprint-12-signoff.md`
- Modify: `src/Services/Auth/AGENTS.md` (one new line under Sprint-11 Picker baseline note)
- Modify: `README.md` (shield + current-stage; demote Sprint-11 to history block)
- Modify: `CLAUDE.md` (current-stage; demote Sprint-11 to history block)
- Modify: `docs/CHANGELOG.md` (new Sprint-12 entry)

**Approach:**

Sign-off doc body mirrors Sprint-11 sign-off structure:
- Tag, sign-off path, plan path, brainstorm path
- Units shipped with commit SHAs
- KTDs (this plan's KTD1-KTD10 list)
- Trade-offs carried forward (full list from origin Scope Boundaries)
- Deviations from plan file list (whatever surfaces during execution — KTD2 saga state name + KTD4 parallel fixture both already pre-emptively documented; expect 1-2 net-new deviations)
- Verification gates met

Auth AGENTS.md gains one new line under the existing Sprint-11 Picker baseline rule:

> **Sprint-12 Dispatcher baseline**: `RolePermissionsSeed` pre-seeds Dispatcher with 3 keys: `outbound.orders.read` + `outbound.orders.ship-confirm` + `hub.connect`. Same KTD1 additive-only re-seed contract as Picker. Owner-manually-granted keys on Dispatcher are preserved across re-seed; Owner deletions revert. **Dispatcher MFA recommended for production despite engine default**: `outbound.orders.ship-confirm` triggers real carrier-API label creation with cost implications — categorically different from Picker's internal saga transition. Operators SHOULD set `users.mfa_required = true` on Dispatcher accounts via the Owner admin surface until Sprint-13 hardens non-Owner MFA defaults. Documented as deployment-time guidance, not enforced by the engine.

CHANGELOG entry follows the Sprint-11 + Sprint-10.5 shape (Tag / Shipped / Documented limitations / Next).

Tag `v0.16.0-sprint-12` annotated; tag message mirrors Sprint-11 tag message structure.

After tag: `git push -u origin feat/sprint-12-second-non-owner-role && git push origin v0.16.0-sprint-12` per the user's `push-before-phase-switch` memory.

**Test scenarios:** N/A (docs + tag).

**Verification:** All Success Criteria items in origin doc satisfied: build 0/0, Migrate.UnitTests + IntegrationTests pass, Handoff E2E + denial tests Skip-marked locally, Vitest 474 + new ConfirmShip coverage pass, sign-off landed, tag pushed.

---

## Scope Boundaries

### Deferred for later

See origin: "Scope Boundaries → Deferred for later" section. Sprint-12 carries forward unchanged:

- **MarkShipFailed failure path + saga compensation** → Sprint-12.5 or later.
- **`auth_audit_log` write-path wiring** on `ConfirmShipHandler` (and Sprint-11's pick handlers) → Sprint-11.5 / Sprint-12 follow-up workstream.
- **Picker / Dispatcher MFA enforcement** → Sprint-13+ hardening decision.
- **Force-change-on-first-login enforcement** → future production hardening.
- **Packer as a fourth role** → out of scope; Pack stays Owner-only.
- **Dispatcher-specific UI views** ("My ship queue") → future sprint when product surface justifies.
- **Observability dashboards for per-role denial rates per tenant** → Phase-3 polish.
- **One-time migration to revoke overlapping keys from Picker** → KTD1 contract holds; operator-runbook audit step instead.

### Outside this product's identity

See origin. Unchanged:
- Runtime-creatable roles via admin UI (closed enum).
- Admin-UI-accessible permission-grant audit trail (storage layer exists, UI does not).

### Deferred to Follow-Up Work

- **Generalize `PickerFixture` ↔ `HandoffFixture` into a shared multi-role fixture** (KTD4 deferred work). If Sprint-13 adds a fourth integration test that needs the same multi-role setup, the duplication will start hurting. Sprint-12.5 or Sprint-13 candidate.
- **`dispatcherBaseline.ts` ↔ backend `DispatcherBaseline` contract test** (KTD9 deferred). Revisit if drift surfaces in practice.
- **Secondary fact querying `outbound_saga_transitions` table post-hand-off** (U4 optional). Defer to Sprint-12.5 if it adds material fixture complexity at execution time.

---

## Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| **R-1** Hand-off E2E test flakes in CI due to 3-transition polling timing | Medium | Medium | KTD7 30s budget = 3 × 10s per-transition polls. Zero-flake mock shipping provider (KTD5) eliminates the carrier-retry variable. If flake > 1-in-20 raise per-transition timeout to 15s as a deviation. |
| **R-2** Cross-role denial test reveals a real Sprint-10 policy regression | Low | High | This is the test's purpose — mitigation IS the test. If U5 fails on a fresh run that's a Sprint-10 regression caught pre-deploy. |
| **R-3** 4th `createIdempotentMutation` consumer breaks Sprint-7 + Sprint-11 mutations | Low | High | Vitest 474-baseline preservation explicit in R10. Refactor is additive (no factor signature change). Per-component coverage on `confirmShip` mirrors Sprint-11 `confirmPick` exactly. |
| **R-4** Tenant in the wild has Picker manually granted `ship-confirm`; Sprint-12 deploy creates a 2-role overlap operator didn't intend | Low | Medium | KTD1 documented behavior + operator-runbook step in CHANGELOG. Sprint-13+ candidate: RolePermissionsEditor UI lint flagging cross-role overlaps on save. |
| **R-5** Dispatcher MFA absence under multi-tenant scale — categorically elevated vs Picker because `outbound.orders.ship-confirm` triggers real carrier-cost actions | Medium | High | Plan documents production-deployment requirement in Auth AGENTS.md (U6): "Operators SHOULD set `users.mfa_required = true` on Dispatcher accounts until Sprint-13 hardens non-Owner MFA defaults." Owner is `MfaRequired=true` by engine default per R17; non-Owner default = false stays at Sprint-12. Engine-level enforcement is Sprint-13 work. **Impact upgraded from Medium → High vs origin** (security-lens F4) to reflect the irreversibility + financial-side-effect difference between Picker pick-confirm (internal saga) and Dispatcher ship-confirm (external carrier API + label cost). |
| **R-6** New ConfirmShip section breaks a11y harness or existing detail-route render | Low | Low | U3 per-component Vitest covers 6 visibility states + 2 interactions. Sprint-11.5-style harness extension stays deferred. |
| **R-7** `HandoffFixture` extracts ~85% from `PickerFixture` and they drift over time | Medium | Low | Acknowledged carrying cost (KTD4). Listed under "Deferred to Follow-Up Work" so Sprint-12.5 or Sprint-13 can consolidate. |
| **R-8** Zero-flake `MockShippingProvider` registration via `ConfigureTestServices` doesn't propagate to `OrdersController.ConfirmShipAsync` | Medium | High | KTD5's factory-form `RemoveAll<IMockShippingProvider>` + `AddSingleton(sp => WithFlakeRate(sp.GetRequiredService<ResiliencePipeline>(), 0.0))` is the standard WAF singleton-replacement pattern; runs after `ConfigureServices` per WAF semantics. Verify at U4 execution time with a smoke probe: resolve `IMockShippingProvider` from `Factory.Services` post-init and assert `is MockShippingProvider` with the zero-flake state. The IConfiguration fallback (`Shipping:FlakeRate`) is NOT available today — it would require constructor refactoring on `MockShippingProvider`; tracked but not in Sprint-12 scope. |
| **R-9** Saga MT message dispatch + EF saga persistence latency between transitions exceeds the per-transition 10s budget | Medium | Medium | Each transition involves controller `SaveChangesAsync` → `_publishEndpoint.Publish(...)` to MT → saga consumer reads + persists in its own EF transaction. Cold CI Docker tier adds broker/consumer startup latency. Mitigation: fixture awaits `IBusControl.WaitUntilStarted()` before issuing the first POST; each transition logs wall-time so flakes are observable. If 10s per-transition proves too tight, raise to 15s as a deviation. KTD7 30s total budget remains the ceiling. |

---

## Dependencies & Prerequisites

See origin "Dependencies" section. All verified during planning:

- ✅ Sprint-11 tagged + pushed (`v0.15.0-sprint-11` confirmed).
- ✅ `UserRole.Dispatcher` enum value exists (`src/Services/Auth/ShopFlow.Auth.Domain/UserRole.cs:20`).
- ✅ DB CHECK constraint already includes `'Dispatcher'` (verified during Sprint-9 schema migration).
- ✅ `PermissionKeys.OutboundOrdersShipConfirm` exists in `PermissionKeys.All` (Sprint-10 KTD8).
- ✅ Saga `Picked → Packed → Shipped` transitions wired (Sprint-3-redux + Sprint-7).
- ✅ `OrdersController.PackConfirmAsync` + `ShipConfirmAsync` carry per-action `[Authorize(Policy = ...)]` attributes (Sprint-10 U2).
- ✅ `NarrowedJwtBuilder` MSBuild Compile-link pattern (Sprint-10.5 U4 → Sprint-11 KTD4).
- ✅ `MockShippingProvider.WithFlakeRate` factory exists (Sprint-3-redux U6).

No new domain migrations needed. No new schema migrations needed. No new framework upgrades. No external research warranted — the patterns are all in-context from Sprint-11.

---

## System-Wide Impact

- **Tenant provisioning surface (`shopflow-migrate provision` + `seed-owner`)**: Fresh tenants gain 3 additional `role_permissions` rows (Dispatcher baseline). Re-runs preserve Owner additions per KTD1.
- **Frontend bundle**: One new file (`dispatcherBaseline.ts`); one new section + one new button on order-detail; one new factor consumer. Estimated < 1 KB gzipped delta.
- **Backend Auth surface**: No code changes to `AuthAdminController`, `RolePermissionsCommandHandler`, or any policy registration. The Dispatcher role's per-tenant rows are what enables the Sprint-10 policy gates to accept Dispatcher JWTs — no new policies, no new keys.
- **Tests**: 2 new Outbound IntegrationTests files + 1 modified Migrate.UnitTests file + 1 modified Migrate.IntegrationTests file + 2 modified frontend test files. Total net new tests ~33 after doc-review additions (9 unit + 2 integration + 6 frontend mutation + 10 frontend visibility/tracking + 1 axe smoke + 6 denial + 1 happy-path).
- **CHANGELOG operator-runbook callout**: Adds one line per the KTD1 documentation.
- **No impact on**: Inventory module, Channel module, Inbound module, Notification module, Analytics module, observability stack, Gateway routes, AppHost wiring, Aspire resource topology.

---

## Verification Strategy

**Build gate (R9):** `dotnet build ShopFlow.sln` returns 0 errors + 0 warnings across all 47 projects after every commit.

**Unit gate:**
- Migrate.UnitTests: 52 Sprint-11 baseline + 9 new Dispatcher facts (including security-F1 Picker-baseline-isolation guard) → ~61 total.
- Sprint-11 Auth.UnitTests + Inventory.UnitTests + Outbound.UnitTests + Inbound.UnitTests + SharedKernel.UnitTests baselines all preserved unchanged.

**Integration gate (CI Docker tier — Skip-marked locally):**
- Migrate.IntegrationTests: 4 Sprint-11 + 2 new = 6 scenarios.
- Outbound.IntegrationTests: Sprint-11 `PickerHappyPathTests` (1) + Sprint-12 `HandoffWorkflowTests` (1 main + optional 1 secondary) + Sprint-12 `CrossRoleDenialTests` (6 — original 4 + adversarial-F3 ordering-fact + adversarial-F8 union-of-perms behavioral pin) = 8+ Handoff facts.

**Frontend gate:**
- Vitest 474-passing Sprint-11 baseline + ~19 new tests across `useOrderMutations.test.ts` and `$orderId.test.tsx` (6 mutation + 8 visibility + 2 interaction + 1 axe + 2 tracking-pill).
- Pre-existing Sprint-7 a11y failures (4) unchanged.

**Sign-off gate:** All Success Criteria items in origin "Success Criteria" section satisfied. Sign-off doc landed. Tag pushed.

---

## Execution Posture

Default pragmatic execution **except for U4**, which carries a test-first `Execution note` because the failing-test-against-under-provisioned-fixture forces the Dispatcher seed step to land correctly the first time. All other units follow the established Sprint-11 cadence (write + test in parallel; pin Sprint-11 baseline tests as regression gates).

---

## Outstanding Questions (resolve during implementation)

- **U-decision** — Sprint-11 fixture uses raw `INSERT` for user seeding (Path B). The `HandoffFixture` will need to repeat that pattern 3 times. If the inserts grow gnarly (Argon2 hash generation, etc.), consider a small `SeedTenantUsers(this AuthDbContext, params (Guid id, string email, UserRole role)[] users)` helper. Discoverable at execution time when the third raw INSERT starts looking copy-pasted.
- **U-decision** — Owner pack-confirm in the U4 happy path needs an `ActualWeightTotal` body. Pick a value that doesn't flag the weight-variance warning (Sprint-3-redux U6's threshold). Match whatever Sprint-3-redux `PackShipEndpointTests` uses if discoverable.
- **U-decision** — `HandoffWorkflowTests` could optionally extend to assert `outbound_saga_transitions` rows are recorded correctly across the 3 transitions. If the helper cost is < 30 lines, fold it in; else defer per "Deferred to Follow-Up Work".
- **U-decision (adversarial-F5)** — When the U4 fixture seeds an order directly to `AwaitingPick` via DbContext writes, it bypasses the production `OrderPlacedV1 → ReserveStockV1 → StockReservedV1` chain that writes `reservations_ledger` rows. `ConfirmShipAsync` enqueues a `ConfirmStockV1` event that Inventory's `ConfirmStockConsumer` consumes. If that consumer reads `reservations_ledger` to find rows to flip and finds none, it may emit an error event the saga routes to `CompensatingReservation` instead of progressing to `Shipped` — making AE2 fail. Verify at U4 execution time by either (a) seeding minimal `reservations_ledger` rows for the order's SKUs alongside the `orders` + `saga_state` writes, OR (b) reading `ConfirmStockConsumer` to confirm it tolerates absent reservation rows for test-seeded orders. If (a) is required, factor a `SeedOrderWithReservationLedger(...)` helper into `HandoffFixture`. Document the outcome in U4 deviations.

---

**Origin**: this plan is the durable output of the 2026-05-22 planning session sourced from `docs/brainstorms/2026-05-22-sprint-12-second-non-owner-role-requirements.md`. The plan resolves all 4 origin "Outstanding Questions (resolve during planning)" — parallel sections on the order-detail surface (KTD4 — keep Sprint-11 patterns symmetric); parallel `HandoffFixture` (KTD4 — Sprint-11 isolation); no `dispatcherBaseline` contract test (KTD9 — per-component coverage suffices); version bump `v0.16.0-sprint-12` (KTD8 — minor matches feature precedent).

**Doc-review pass (2026-05-22)**: 5 reviewers dispatched (coherence + feasibility + design-lens + security-lens + adversarial). 1 P0 architectural finding caught at cross-persona agreement confidence 100 (KTD2 saga-state-vs-Order.Status conflation — `OrderDetailDto.currentSagaState` is sourced from `saga_state.CurrentState` which never reaches `'AwaitingShip'`; the Order aggregate's `status` field does). Resolved via Option A (gate U3 on `detail.status`, poll U4 for `'Packed'` mid-flow + verify `orders.Status === 'AwaitingShip'` via the GET response, terminal poll `'Shipped'`). 7 P1 fixes applied in-place: KTD5 factory-form override (feasibility-F2); MT bus readiness wait + per-transition wall-time logging (adversarial-F4); perm-before-state ordering test in U5 (adversarial-F3); union-of-perms behavioral pin in U5 (adversarial-F8); Picker-baseline-isolation guard in U1 (security-F1); AGENTS.md MFA-deployment guidance + R-5 impact upgrade (security-F4); ConfirmShip error-recovery behavior + axe smoke test + persistent tracking-pill in U3 (design-F1 + design-F2 + design-F3 + design-F4). 1 safe_auto fix applied silently (U1 `InsertAsync` 5-arg signature). 5 advisory observations carried forward as Deferred-to-Follow-Up-Work.
