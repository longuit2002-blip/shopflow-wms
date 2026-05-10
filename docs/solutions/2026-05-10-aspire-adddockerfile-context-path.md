# Aspire `AddDockerfile` — `contextPath` is repo-relative-to-AppHost-csproj, NOT to repo root

**Date**: 2026-05-10
**Affects**: [`src/AppHost/ShopFlow.AppHost/Program.cs`](../../src/AppHost/ShopFlow.AppHost/Program.cs) (U9 deliverable)

## Problem

The mock-channel servers (U7) ship two Dockerfiles whose build context is
`infrastructure/mock-channels/` (not the per-server folder), because each
Dockerfile `COPY`s the sibling `_shared/` library — see
[2026-05-10-mock-channel-shared-library-pattern.md](2026-05-10-mock-channel-shared-library-pattern.md).

Aspire's `builder.AddDockerfile(name, contextPath, dockerfilePath)` accepts
`contextPath` as a path resolved against the AppHost csproj directory, not
the repo root. The intuitive value `infrastructure/mock-channels` (which
would be correct from the repo root) silently fails to find the Dockerfile
because Aspire's resolver treats it as
`src/AppHost/ShopFlow.AppHost/infrastructure/mock-channels`. The error is
`Cannot find Dockerfile`, several layers down the stack, and only surfaces
at `aspire run` (not at `dotnet build`).

## Root cause

Aspire's hosting SDK resolves resource paths relative to the AppHost
project directory because that is where `Program.cs` lives and where most
in-process resource artifacts (project references, launch profiles) are
located. Docker build contexts are an exception in our setup: the build
context lives outside the AppHost folder, three levels up.

## Solution

Pass `contextPath` as an explicit relative path that walks up to the repo
root, then down into `infrastructure/mock-channels/`:

```csharp
builder.AddDockerfile(
    "shopee-mock",
    contextPath: "../../../infrastructure/mock-channels",
    dockerfilePath: "shopee-mock/Dockerfile")
```

The `dockerfilePath` is resolved relative to `contextPath`, which matches
the bare `docker build -f shopee-mock/Dockerfile infrastructure/mock-channels/`
invocation documented in the Dockerfile header comments.

## Prevention

- When adding a new mock-channel marketplace (TikTok Shop, Shopify), copy
  the `AddDockerfile` block verbatim and only change the resource name +
  `dockerfilePath`. The `contextPath` stays the same — `_shared/` is a
  cross-marketplace concern by design.
- If the AppHost project ever moves (e.g., promoted out of `src/AppHost/`),
  the `../../../` count must change. Document the move with a one-line
  ADR amendment so this entry is updated alongside.
- For Compose, the equivalent `build.context: ./mock-channels` works
  because `docker-compose.yml` lives at `infrastructure/docker-compose.yml`
  — Compose resolves the context relative to the file's directory. The two
  manifests use different relativization rules; both encode the same
  underlying constraint.

## References

- [`src/AppHost/ShopFlow.AppHost/Program.cs`](../../src/AppHost/ShopFlow.AppHost/Program.cs) — both `shopee-mock` and `lazada-mock` resources.
- [`infrastructure/docker-compose.yml`](../../infrastructure/docker-compose.yml) — the parallel encoding for the Compose path.
- [2026-05-10-mock-channel-shared-library-pattern.md](2026-05-10-mock-channel-shared-library-pattern.md) — the `_shared/` build-context constraint that drives both encodings.
- ADR-0001 — Aspire AppHost dev-only; Compose for production handoff.
