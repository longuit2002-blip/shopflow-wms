# ADR-0001: Aspire AppHost for local dev, hand-maintained Docker Compose for production

- **Status**: Accepted
- **Date**: 2026-04-27
- **Deciders**: solo dev (longuit2002-blip)
- **Supersedes**: —
- **Superseded by**: —

---

## Context

ShopFlow WMS needs an orchestrator for its dev stack: Postgres 16, Redis 7, RabbitMQ 3, OTel collector + Tempo + Prometheus + Seq, MinIO, Shopee + Lazada mock-channel servers (Node/Express), the API gateway, and 6 service modules (Inventory blessed first, then the rest replicated in W2).

In April 2026 the .NET ecosystem has two viable answers:

1. **`docker compose` with a hand-maintained `infrastructure/docker-compose.yml`** — the original choice in the technical design (`02-technical-design-document.md.docx` §18.1, ADR-10). Mature, orchestrator-agnostic, no cloud lock-in. Cold start across ~13 containers risks pushing past the Phase-0 < 90s gate (`01-product-development-plan.md.docx` §8.2, plan §293). Hand-tuning compose, healthchecks, `depends_on`, and per-image volume mounts is real solo-dev tax across 12 weeks.
2. **`dotnet aspnet/aspire` AppHost (`aspire run`)** — went GA earlier in 2025-2026. Replaces the dev-loop role of compose with a `Program.cs`-style declarative AppHost that registers resources (containers, projects, Node executables) and produces a dashboard with OTel traces/metrics/logs for free. DNS-based discovery (`redis.localhost`) eliminates connection-string juggling. `dotnet/eShop` — Microsoft's current reference architecture and the closest public analog to a .NET 8/9 microservices portfolio — uses Aspire as its primary local orchestrator. Aspire's deployment-target story for plain Docker Compose (Compose output as a build artifact) is *in progress* as of 2026 — not first-class — which conflicts with the design's stated "no cloud lock-in" non-functional requirement (`02-technical-design-document.md.docx` §165, ADR-10) if Aspire is treated as the authoritative production manifest.

The user's stated cross-machine workflow (zip + ship across multiple computers) makes inner-loop friction more expensive than for a single-machine dev — every time we re-clone we pay the setup tax — which sharpens the case for an orchestrator that minimizes that tax.

## Decision

**Use Aspire AppHost as the source-of-truth for local development only. Maintain `infrastructure/docker-compose.yml` separately as the authoritative production-handoff manifest.** Aspire is not the deployment system; the hand-maintained compose file is. The two are kept in deliberate parallel; drift between them is caught by a thin smoke check in CI (the same set of services must come up under both).

For Phase 0, this means:

- `src/AppHost/ShopFlow.AppHost/Program.cs` registers all dev resources (Postgres, Redis, RabbitMQ, Tempo, Seq, Prometheus, MinIO, mock-channels, all module APIs, gateway).
- `task up` runs `aspire run` against this AppHost. Cold-start gate target: < 60s on a developer laptop with .NET 8 SDK and Docker Desktop (note: tighter than the < 90s plan gate, since Aspire's in-process orchestration typically beats compose).
- `infrastructure/docker-compose.yml` is hand-maintained, lists the same external services (no module API entries — production deployment is per-service via `dotnet publish` into containers), and is exercised in CI as a sanity check that both manifests describe the same stack.
- The Phase-0 < 90s cold-start gate in CI uses Aspire (`aspire run` headless mode); the compose smoke check is a separate CI step that verifies `docker compose up --wait` for the infra-only services completes in < 60s.

## Rationale

- **Inner-loop velocity matters most for solo-dev Phase 0-1.** Free OTel dashboard, DNS-based discovery, and one-binary orchestration ("F5 → coherent system") compound across 10 weeks of daily debugging. Compose's hand-tuning surface is a tax on every iteration.
- **`dotnet/eShop` reviewer optic.** A 2026 senior-engineer reviewer who looks at the repo will pattern-match against eShop's Aspire orchestration. Choosing compose-only without an articulated rationale signals "didn't track the ecosystem." Choosing Aspire matches the de-facto reference.
- **No-cloud-lock-in is preserved by treating Aspire as dev-only.** Aspire's AppHost code does not ship as a runtime dependency of any service; the modules are plain ASP.NET Core hosts that can run under any orchestrator. The hand-maintained compose file remains the authoritative production manifest, so the `02-technical-design-document.md.docx` §165 commitment ("no AWS-only primitives in the critical path") holds without modification.
- **Drift detection is cheap.** A CI step that diff-checks the named containers between `ShopFlow.AppHost.Program.cs` and `infrastructure/docker-compose.yml` catches divergence within a single PR.

## Consequences

### Positive

- Inner-loop dev gains the Aspire dashboard from W1 (correlation traces visible in U6 Inventory module from day one).
- Phase-0 cold-start has a realistic < 60s target instead of fighting compose's 90s ceiling.
- Reviewer-readable: the AppHost is the architecture diagram-as-code.

### Negative

- **Two manifests instead of one.** Drift risk between AppHost and compose; mitigated by the CI diff check.
- **Aspire learning curve.** The first time `Program.cs` is wired (U9), there is genuine ramp-up cost. Mitigated by `dotnet/eShop` as a reference repo and Aspire's official samples.
- **Aspire AppHost is .NET-specific.** Mock-channel servers (Node/Express, U7) are registered as Aspire `AddNpmApp(...)` resources, which is a slightly less ergonomic path than a plain compose service block. The mocks remain runnable under compose for cases where Aspire is not present.
- **Phase 4 ship requires authoring per-orchestrator deployment manifests** (k8s Helm chart, ECS task definition, Nomad job — `02-technical-design-document.md.docx` §18.2). The hand-maintained compose file is one of those targets; Aspire's preview Compose-output-as-build-artifact may help, but is not relied on.

### Neutral

- The Phase-0 deliverable list grows by one item (the smoke-check CI step), but shrinks by avoiding compose-tuning that would otherwise consume 1-2 days of W1.

## When this breaks

Revisit this ADR if any of the following hold:

1. **Aspire's Compose-output-as-build-artifact graduates to GA and matches our requirements.** At that point the hand-maintained compose file may become a generated artifact, simplifying drift management. New ADR supersedes this one.
2. **A deployment target rejects Aspire-shaped manifests.** Unlikely (Aspire deploys to plain containers), but if e.g., a managed-host vendor requires a vendor-specific compose dialect we cannot derive, the hand-maintained compose file remains authoritative — which this ADR already accommodates, so no supersession needed.
3. **Cold-start under Aspire exceeds 60s on developer hardware.** Trigger to investigate compose profiles, image-tag pinning, or cold-cache prefetch. If Aspire genuinely cannot meet the gate, fall back to compose-only and supersede this ADR.
4. **The `dotnet/eShop` reference deprecates Aspire** or changes orchestration. Unlikely in 2026 but worth a 1-paragraph sanity check at Phase-0 sign-off.

## References

- `02-technical-design-document.md.docx` §1 ADR-10, §18.1, §165 (no-cloud-lock-in)
- `01-product-development-plan.md.docx` §8.2, §293 (Phase-0 cold-start gate)
- `docs/ideation/2026-04-27-shopflow-wms-bootstrap-ideation.md` idea #2
- `docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md` U9 (CI + dev orchestrator wiring)
- External: github.com/dotnet/eShop, github.com/microsoft/aspire/discussions/10644 (roadmap)
