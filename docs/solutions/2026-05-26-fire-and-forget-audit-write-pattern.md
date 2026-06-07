---
title: "Fire-and-forget audit-write pattern (best-effort, NOT latency-isolated)"
date: 2026-05-26
type: convention
sprint: 12.5
units: [U1]
---

## Rule

When a command handler writes a row to `auth_audit_log` (or any future per-tenant audit/observability table), wrap the awaited `AppendAsync(...)` in try/catch + `_logger.LogWarning(ex, ...)` at the handler call site. Audit-write FAILURES must never propagate to the HTTP response.

Centralise the wrapper so handlers don't repeat boilerplate. In Sprint-12.5 U1 this is `ShopFlow.Auth.Application.Audit.AuthAuditWriter.TryAppendAsync(...)`.

## Why

Audit trails record state changes that already happened. If the audit-table write fails after the action succeeded, the operational signal (login response, password change, MFA disable) must still reach the caller cleanly — turning a successful login into a 500 because the audit table is down inverts the actual outcome.

Conversely, if the audit-table write fails before the action commits, we still want the action to proceed (the audit row is best-effort). The handler simply skips the audit row and logs a Warning so production traces catch the audit-table outage without contaminating the auth response.

## Critical framing — best-effort, NOT latency-isolation

This pattern is **best-effort with exception suppression**, NOT **latency isolation**. The handler still `await`s the underlying `AppendAsync(...)` (which itself does `await AddAsync + await SaveChangesAsync` per the EF impl) — a slow Postgres on the tenant DB will increase the calling auth path's response p99 even when the audit write eventually succeeds.

Acceptance trade-off at Sprint-12.5:
- Audit writes are small + fast inserts to a single table.
- Tenant DBs use PgBouncer transaction pooling, so connection acquisition latency is bounded.
- The audit table has no contention beyond inserts (append-only).
- Production traces are wired (Sprint-0 redux OpenTelemetry baseline).

If production traces show audit latency contaminating login p99, evolve to a background-channel dispatch (e.g., `Channel<AuditEntry>` + a `BackgroundService` that drains the channel into the audit table out of the request critical path). That evolution is a Sprint-13+ workstream — Sprint-12.5 accepts the in-request coupling because the gap (Sprint-9 ships storage, no handler writes) is the canonical bug, not the latency profile.

## Dependency on ForwardedHeaders middleware

The audit row's `source_ip` column is sourced from `HttpContext.Connection.RemoteIpAddress?.ToString()` at the controller boundary. That value is correct **only if** the SharedKernel `ForwardedHeaders` middleware (Sprint-9 KTD7) has rewritten the remote IP from the load-balancer's `X-Forwarded-For` header before the controller action runs.

Auth.Api's `Program.cs` calls `UseShopFlowSecurityPipeline()` which wires the middleware. The boot guard requires `KnownProxies + KnownNetworks` in non-Development environments (silent disable + spoofing vector if absent). If a future deployment misconfigures this, the boot guard throws — fail-closed is correct.

Reviewing any new handler that audits an operator-initiated action:
1. Confirm the controller path runs after `UseShopFlowSecurityPipeline()` in `Program.cs`.
2. Confirm `HttpContext.Connection.RemoteIpAddress` is read at the controller (not inside the handler — the handler receives the value via the command record).
3. The command record's `SourceIp` field is `string` (not `IPAddress?`) — the controller normalises to "unknown" when null so the audit row never carries a NULL source_ip.

## How to apply

In any Auth command handler that maps to one of the documented `EventType` keys:

```csharp
public sealed class MyHandler : IRequestHandler<MyCommand, Result>
{
    private readonly IAuthAuditLogRepository _auditLog;
    private readonly ILogger<MyHandler> _logger;
    // ... other deps ...

    public async Task<Result> Handle(MyCommand request, CancellationToken ct)
    {
        // ... business logic that returns success ...

        await AuthAuditWriter.TryAppendAsync(
            _auditLog,
            _logger,
            AuthAuditEventTypes.MyEventType,
            userId: request.UserId,
            sourceIp: request.SourceIp,
            userAgent: request.UserAgent,
            metadata: new { /* structured payload */ },
            correlationId: request.CorrelationId,
            ct).ConfigureAwait(false);

        return Result.Success();
    }
}
```

Rejection paths (validation failures, OwnerCritical guard hits, `auth.invalid_credentials` collapses) generally do NOT audit — audit captures successful actions. Two deliberate exceptions in Sprint-12.5 U1:
- `auth.login.failed` audits on every failed credential check (even unknown-email, with `userId: null` + the submittedEmail in metadata per KTD9 forensic trade-off).
- `auth.login.locked` audits **once** at the lockout boundary attempt (not on subsequent silent-locked retries) — matches `AccountLockedV1` cross-module event semantics.

## Why not centralise audit inside the repository

The `IAuthAuditLogRepository` contract is the **storage** port. Failure-handling policy belongs at the consumer (handler) because:
- Some handlers emit multiple `EventType` keys from one command (Login emits 1-3 depending on outcome).
- The metadata shape varies per event type — repositories don't shape payloads.
- Tests substitute the repository at the handler boundary; centralised failure-suppression inside the repository would obscure the contract that tests pin.

## Why structured metadata, not free-form strings

`metadata_json` is `text NOT NULL` in `auth_audit_log`. Sprint-12.5 U1 standardises on `System.Text.Json.JsonSerializer.Serialize(...)` via `OutboxJsonOptions.Default` (camelCase, case-insensitive deserialize). Empty metadata writes `"{}"` (NOT empty string / NULL) so downstream consumers can always `JSON.parse(...)` the column without null-checks.

The 15 documented metadata shapes:

| EventType | Metadata shape |
|---|---|
| `auth.login.success` | `{"tenantSlug":"...","rememberMe":bool}` |
| `auth.login.failed` | `{"reason":"auth.invalid_credentials","submittedEmail":"..."}` |
| `auth.login.locked` | `{"lockedUntil":"iso-8601-utc"}` |
| `auth.refresh.success` | `{"chainId":"uuid"}` |
| `auth.refresh.reused` | `{"chainId":"uuid","revokedAt":"iso-8601-utc"}` |
| `auth.logout` | `{}` |
| `auth.password.changed` | `{}` |
| `auth.password.reset.requested` | `{}` |
| `auth.password.reset.completed` | `{}` |
| `auth.mfa.enrolled` | `{}` |
| `auth.mfa.used` | `{}` |
| `auth.mfa.disabled` | `{}` |
| `auth.mfa.reset_by_owner` | `{"targetUserId":"uuid"}` |
| `auth.account.unlocked_by_owner` | `{"targetUserId":"uuid"}` |
| `auth.role_permissions.changed` | `{"targetRole":"...","added":[...],"removed":[...]}` |

The `mfa.reset_by_owner` / `account.unlocked_by_owner` / `role_permissions.changed` shapes carry actor-vs-target separation: the audit row's `user_id` column is the ACTOR, and `metadata.targetUserId` / `metadata.targetRole` identifies the SUBJECT.

## Tests pin

Per-handler unit tests assert `_auditLog.Received(1).AppendAsync(<eventType>, ...)` on every terminal emit path, AND `_auditLog.DidNotReceive().AppendAsync(...)` on every rejection path that shouldn't audit. The contract is "exactly one row per terminal emit"; missing rows surface immediately, double-emission surfaces immediately.

The Sprint-12.5 U1 happy-path Docker-backed integration test at `tests/ShopFlow.Auth.IntegrationTests/Authorization/AuthAuditLogIntegrationTests.cs` exercises the full path (HTTP → controller → handler → repo → Postgres) and asserts on the persisted row's content.

The fire-and-forget contract has its own integration test in the same file: dropping the `auth_audit_log` table mid-request asserts the auth response surface stays clean (401, not 500).
