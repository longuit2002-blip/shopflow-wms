---
name: jwt-token-issuer-singleton-consumes-scoped
description: JwtTokenIssuer (ITokenIssuer) was registered Singleton but consumes the scoped IRolePermissionRepository (added Sprint-9 U6). DI scope validation rejects this — the Auth.Api WAF (ValidateOnBuild) and the Aspire dev host (ValidateScopes in Development) both throw at startup, so Auth.Api never served HTTP. Found the first time the Auth WAF actually booted (every Auth integration test was Skip-marked).
metadata:
  type: bug
  date: 2026-05-27
  tags: [di, lifetime, singleton-scoped, auth, aspire, webapplicationfactory, finish-line]
---

# JwtTokenIssuer: Singleton consuming a Scoped service

## Symptom

Booting `Auth.Api` via `WebApplicationFactory<Program>` (the first time any
Auth integration test actually ran) threw at host build:

```
System.AggregateException : Some services are not able to be constructed
  ... Cannot consume scoped service 'ShopFlow.Auth.Application.Ports.IRolePermissionRepository'
      from singleton 'ShopFlow.Auth.Application.Ports.ITokenIssuer'.
  ... Cannot consume scoped service 'ShopFlow.Auth.Infrastructure.AuthDbContext'
      from singleton 'ShopFlow.Auth.Application.Ports.ITokenIssuer'.
```

This very likely also explains the unconfirmed item #8 in
[2026-05-27-aspire-dev-stack-first-boot-repairs.md](./2026-05-27-aspire-dev-stack-first-boot-repairs.md)
("Auth.Api / StockSync.Api / Notification.Api did not come up as listening
HTTP processes"): the Aspire dev host runs in the Development environment,
where the host enables `ValidateScopes` (and `ValidateOnBuild`) by default —
so the same descriptor-validation error would throw at Auth.Api startup and
the service would never bind its port. (Confirm against the live Aspire boot
in the dev-stack repair unit.)

## Root cause

`JwtTokenIssuer` was registered `AddSingleton<ITokenIssuer, JwtTokenIssuer>()`
back in Sprint-8, when its only dependencies were immutable per-process
(the signing key + `JsonWebTokenHandler`). The comment even justified it:
"Singleton because the handler + signing key are immutable per-process."

Sprint-9 U6 then gave the issuer a constructor dependency on
`IRolePermissionRepository` (to project the `perm[]` claim from the per-tenant
`role_permissions` table) — and that repository is `Scoped` (it rides the
per-request `AuthDbContext`). A Singleton may not capture a Scoped dependency
(it would pin one request's DbContext for the process lifetime), so the DI
container rejects the graph during scope validation.

The bug passed `dotnet build`, the full unit suite, and doc-review because:
- nothing constructs `JwtTokenIssuer` through the container in a unit test
  (the unit tests `new` it directly with a substitute repo), and
- every Auth integration test that boots the real container was
  `[Fact(Skip = "...CI runs it")]` — and a hardcoded `Skip` is not removable
  by a `dotnet test --filter`, so those tests ran **nowhere**, including CI.
  The validation error had no execution path that would surface it.

## Fix

Register the issuer `Scoped`:

```csharp
// src/Services/Auth/ShopFlow.Auth.Infrastructure/AuthServiceCollectionExtensions.cs
services.AddScoped<ITokenIssuer, JwtTokenIssuer>();
```

`JwtTokenIssuer` holds no cross-request mutable state; the `JsonWebTokenHandler`
and `SymmetricSecurityKey` are cheap to construct per scope. Scoped is the
correct lifetime once a scoped dependency entered the constructor.

## Lessons

1. **When you add a dependency to a service, re-check the consumer's lifetime.**
   A Singleton silently becomes invalid the moment it captures a Scoped (or
   Transient-that-captures-Scoped) dependency. The compiler won't catch it; only
   DI scope validation will — and only if something actually builds the graph.
2. **Skip-marked integration tests that run nowhere are worse than missing
   tests** — they advertise coverage that doesn't exist and let
   composition-root bugs (DI lifetimes, startup guards, config binding) accrue
   undetected. A conditional opt-in (run locally on demand + automatically in
   CI) is the fix; see the finish-line `ProofGate`.
3. **`WebApplicationFactory` enables `ValidateOnBuild` + `ValidateScopes`** — a
   WAF boot is a real composition-root smoke test. The same validation the
   Aspire dev host applies in Development. If a service won't boot under the
   WAF, it won't boot under `task up` either.
