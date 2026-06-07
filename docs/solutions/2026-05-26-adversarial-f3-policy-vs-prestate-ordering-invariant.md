---
name: adversarial-f3-policy-vs-prestate-ordering-invariant
description: For any [Authorize(Policy)]-gated saga-touching endpoint, the authorization filter MUST fire before the controller's pre-state check. Sprint-12 U5 pinned this for ConfirmShip; Sprint-12.5 U3 generalizes to all policy-gated saga-touching endpoints.
metadata:
  type: convention
  sprint: 12.5
  tags: [authorization, rbac, saga, sprint-12.5, sprint-12, testing]
  severity: medium
---

# Adversarial-F3 ordering invariant: policy filter fires before pre-state check

When a controller action is gated by `[Authorize(Policy = ...)]` AND has a pre-state guard (e.g., `if (order.Status != AwaitingShip) return 422 order.invalid_state`), the authorization filter MUST evaluate the policy BEFORE the controller method body runs. A caller who lacks the policy permission gets a `403 auth.forbidden`, NOT a `422 order.invalid_state`.

## Why this matters

If the pre-state check ran first, an attacker probing a permission boundary could distinguish "I don't have the permission" from "I have the permission but the order is in the wrong state" — leaking state information through error codes. The framework's filter ordering (`AuthorizationFilter` runs in stage 2 of the MVC pipeline, before `ActionFilter` / `ResultFilter` / the action body itself) enforces the invariant; the test discipline below catches accidental regressions.

## How to test

Every policy-gated saga-touching endpoint must carry one adversarial-F3 ordering pin in `CrossRoleDenialTests` (or equivalent). The test scenario:

1. Build a JWT for a role that lacks the endpoint's policy permission.
2. Put the order in a state that would ALSO fail the pre-state guard if the auth filter were skipped.
3. POST to the endpoint.
4. Assert response is `403` with errorCode `auth.forbidden` — NOT `422 order.invalid_state`, NOT `409 *_already_recorded`.

Example: Sprint-12 U5 `CrossRoleDenialTests` `Dispatcher_AttemptsPickConfirm_OnAwaitingShipOrder_Returns403_NotStateError` — a Dispatcher (lacks `outbound.orders.pick-confirm`) calls `/confirm-pick` on an order in AwaitingShip (would fail pre-state if auth were skipped). Asserts 403.

Sprint-12.5 U3 generalizes this to the new `mark-ship-failed` endpoint: a Picker (lacks `outbound.orders.ship-confirm`) calling mark-ship-failed must return 403, regardless of the order's current status.

## Test sites that must carry this invariant

Per Sprint-12.5, the policy-gated saga-touching endpoints in `OrdersController.cs` are:

| Endpoint | Policy | Adversarial-F3 pin lives in |
|---|---|---|
| `POST /confirm-pick` | `outbound.orders.pick-confirm` | `CrossRoleDenialTests.Dispatcher_AttemptsPickConfirm_...` (Sprint-12); `Packer_AttemptsConfirmPick_OnCancelledOrder_Returns403_NotStateError` (Sprint-13 U5 — third pin) |
| `POST /mark-pick-failed` | `outbound.orders.pick-confirm` | (inherited; same policy) |
| `POST /confirm-pack` | `outbound.orders.pack-confirm` | pack-confirm policy family pinned via the Sprint-13 third pin (the Packer→confirm-pick pin proves filter-before-prestate for the policy-gated family) |
| `POST /mark-pack-failed` | `outbound.orders.pack-confirm` | Sprint-13 U5 — `Picker_AttemptsMarkPackFailed_...` + `Dispatcher_AttemptsMarkPackFailed_...` |
| `POST /confirm-ship` | `outbound.orders.ship-confirm` | `CrossRoleDenialTests` Sprint-12 baseline |
| `POST /mark-ship-failed` | `outbound.orders.ship-confirm` | Sprint-12.5 U3 new pin |

## Failure mode if violated

If a future endpoint somehow has the pre-state check evaluate before the policy filter (e.g., by accident in a custom middleware), the regression surfaces as: a denial-test asserts `403 auth.forbidden`, but the actual response is `422 order.invalid_state` or `409 *_already_recorded`. The fix is to restore filter ordering, not to weaken the test.

## Sprint-12 → Sprint-12.5 trajectory

- Sprint-12 U5 ships the first adversarial-F3 pin for ConfirmShip.
- Sprint-12.5 U3 ships the second pin for MarkShipFailed and graduates the discipline from "Sprint-12 U5 pinned this" to "class-level invariant for all policy-gated saga-touching endpoints."
- **Sprint-13 U5 ships the third pin** (`Packer_AttemptsConfirmPick_OnCancelledOrder_Returns403_NotStateError`) when Packer is introduced and Pack-confirm moves off Owner. The invariant is now exercised across the pick / pack / ship policy families. The new `mark-pack-failed` endpoint also gains its cross-role denial coverage (Picker + Dispatcher → 403). The discipline is fully generalized: every future policy-gated saga-touching endpoint carries a pin.

## Why this is a separate file from generic ASP.NET filter docs

The Microsoft filter-ordering documentation describes the pipeline in general terms. This file documents the ShopFlow WMS-specific invariant that EVERY policy-gated saga-touching endpoint carries an adversarial-F3 test — so a future contributor adding a new endpoint doesn't need to rediscover the pattern by reading old Sprint sign-offs.
