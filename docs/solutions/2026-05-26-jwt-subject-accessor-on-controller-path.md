---
name: jwt-subject-accessor-on-controller-path
description: Canonical pattern for reading the authenticated operator's user-id on Outbound (and any business-module) controller paths — use IRequestContext.UserId, fall back to User.FindFirstValue(ClaimTypes.NameIdentifier).
metadata:
  type: convention
  sprint: 12.5
  tags: [jwt, controllers, request-context, sprint-12.5, auth]
  severity: low
---

# JWT subject accessor on controller path

When a controller endpoint needs the authenticated operator's user-id (Sprint-12.5 surfaced this requirement for `actor_user_id` audit attribution on Outbound saga transitions), there are two paths:

## Canonical: `IRequestContext.UserId`

```csharp
public sealed class OrdersController : ControllerBase
{
    private readonly IRequestContext _requestContext;

    public async Task<IActionResult> ConfirmShipAsync(Guid id, CancellationToken ct)
    {
        // ...
        await _publishEndpoint
            .Publish(new ShipConfirmed(order.Id, label.LabelUrl, label.TrackingNumber, _requestContext.UserId), ct)
            .ConfigureAwait(false);
    }
}
```

`IRequestContext.UserId` (defined at [src/Shared/ShopFlow.SharedKernel/Application/IRequestContext.cs](../../src/Shared/ShopFlow.SharedKernel/Application/IRequestContext.cs)) is `Guid?` — null for anonymous endpoints (webhook receivers gated by HMAC), populated by `TenantRoutingMiddleware` from the JWT subject claim for authenticated requests. The middleware runs upstream of every business-module controller's `[Authorize(Policy)]` filter, so by the time the action method body runs, the property is fully resolved if the request is authenticated.

## Defensive fallback: direct JWT claim read

Only use this when `IRequestContext.UserId` isn't injected (legacy test ctors, anonymous endpoints that occasionally need to peek at the subject, etc.):

```csharp
private bool TryReadActorId(out Guid actorId)
{
    var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
              ?? User.FindFirst("sub")?.Value;
    return Guid.TryParse(sub, out actorId);
}
```

Reference impl at [src/Services/Auth/ShopFlow.Auth.Api/Controllers/AuthAdminController.cs](../../src/Services/Auth/ShopFlow.Auth.Api/Controllers/AuthAdminController.cs) (`TryReadActorId` method).

## Why prefer `IRequestContext.UserId`

- One source of truth for tenant + correlation + user — re-reading from `User.FindFirst` risks divergence if the middleware ever changes its claim-resolution priority (header > JWT > subdomain per ADR-0003).
- DI-injectable: no `ControllerBase.User` coupling — Application-layer code (handlers, services) can take `IRequestContext` and stay unit-testable without an HTTP context.
- Per [src/AGENTS.md](../../src/AGENTS.md) §3.15 + analyzer `ShopFlow0004`, re-validating tenant identity in handlers is forbidden. Same discipline applies to actor identity — trust `IRequestContext` once populated.

## When to use the defensive fallback

If `IRequestContext.UserId` is null for a request that should have been authenticated (theoretically impossible post-`[Authorize(Policy)]` filter, since the policy requires an authenticated principal), the fallback path catches the anomaly. Sprint-12.5 U3's `MarkShipFailedAsync` writes the result to the saga event payload regardless — a null `actor_user_id` audit row is acceptable (it just means "system-triggered or accessor anomaly"); a server crash is not.

## Sprint-12.5 context

Surfaced as a brainstorm Outstanding Question ("verify and pick the canonical path during planning"). Resolved as KTD4 of the Sprint-12.5 plan. Three handlers wire this pattern: `ConfirmPickAsync`, `MarkPickFailedAsync`, `ConfirmPackAsync`, `ConfirmShipAsync`, and the new `MarkShipFailedAsync`. Actor flows from the controller into the saga event payload (per KTD3 — additive nullable on the event records) and out through `SagaTransitionObserver.RecordAsync` to the new `outbound_saga_transitions.actor_user_id` column.
