---
title: "ASP.NET RateLimiter behind YARP requires ForwardedHeaders BEFORE UseRateLimiter"
date: 2026-05-20
type: architecture
sprint: 9
units: [U7, U9]
---

## Rule

When the rate-limit partition key reads the client IP off `HttpContext.Connection.RemoteIpAddress`, `UseForwardedHeaders` MUST execute BEFORE `UseRateLimiter` in the request pipeline. AND `ForwardedHeadersOptions.KnownProxies` / `KnownNetworks` MUST be configured with the actual gateway IPs/CIDRs in non-Development environments.

## Why

YARP sets `X-Forwarded-For: <client-ip>` on the upstream request. Without `UseForwardedHeaders` wired before the limiter:

- `Connection.RemoteIpAddress` resolves to the gateway's IP (the immediate caller).
- Every legitimate user shares one rate-limit bucket.
- A single user can starve the global capacity; the rate limit silently fails to do its job.

With `UseForwardedHeaders` but an empty `KnownProxies` allowlist:

- The middleware silently DISABLES forwarded-header processing (security default — don't trust untrusted callers).
- Same broken behavior as the no-middleware case.
- AND direct callers can spoof `X-Forwarded-For: <any-IP>` to manipulate the partition key.

## How to apply

In `AddShopFlowDefaults` (kernel composition):

```csharp
services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("127.0.0.0"), 8));
    o.KnownProxies.Add(IPAddress.IPv6Loopback);
    // Operators add gateway IPs via configuration sections.
});

// Startup gate: non-Development requires explicit allowlist.
if (env != Development && KnownProxies.empty && KnownNetworks.empty)
    throw new InvalidOperationException("...");
```

In each module's `Program.cs`:

```csharp
app.UseProblemDetails();
app.UseShopFlowSecurityPipeline();  // ForwardedHeaders + RateLimiter, in order
app.UseAuthentication();
app.UseAuthorization();
app.UseTenantRouting();
app.MapControllers();
```

`UseShopFlowSecurityPipeline` is the kernel-provided helper that wires `UseForwardedHeaders` THEN `UseRateLimiter` in the correct order.

## Where it lives

- `src/Shared/ShopFlow.SharedKernel/Infrastructure/AddShopFlowDefaults.cs`:
  - `Configure<ForwardedHeadersOptions>` config block.
  - `AddRateLimiter` policy registrations (`auth-credentials`, `auth-forgot-password`).
  - `UseShopFlowSecurityPipeline` extension method.
  - Startup gate via `InvalidOperationException`.
- `src/Services/Auth/ShopFlow.Auth.Api/Program.cs#UseShopFlowSecurityPipeline()`.
- `src/Services/Auth/ShopFlow.Auth.Api/appsettings.json#Auth:ForwardedHeaders` (config surface).

## Reviewers' checklist

- Any new business module's `Program.cs` MUST call `UseShopFlowSecurityPipeline` if it issues authentication-rate-limited endpoints.
- Non-Development `Auth:ForwardedHeaders:KnownProxies` or `:KnownNetworks` MUST list the actual gateway IP/CIDR before deploy.
- Sprint-10+: distributed rate-limit store would replace the in-memory `PartitionedRateLimiter` (each Aspire instance has its own bucket today; horizontal scale-out needs Redis-backed shared state).
