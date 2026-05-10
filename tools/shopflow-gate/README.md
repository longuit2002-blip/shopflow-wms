# shopflow-gate

Phase-N scale-gate orchestrator CLI. Runs cold-start / latency / chaos / post-condition checks for a given Phase of the ShopFlow roadmap and exits 0 (pass) or 1 (fail).

## Purpose

Every PR re-runs the gate for every prior phase. A Phase-1 PR runs `shopflow-gate 0` AND `shopflow-gate 1`; a Phase-2 PR runs 0, 1, 2; and so on. This regression-detection pattern is the reason the gate is a CLI rather than a one-off script — the surface stays stable as phases land.

Phase-0 is the only phase implemented today. Its three checks are:

1. **Cold-start** — launch the Aspire AppHost (`aspire run --headless src/AppHost/ShopFlow.AppHost/ShopFlow.AppHost.csproj`), measure wall-clock time from process-start to first 200 from Inventory's `/healthz`. Target: under 90 seconds (ADR-0001 tightens to 60s once Aspire 13.x cold-start lands reliably).
2. **Auth p99** — 100 sequential `POST /api/auth/login` calls against the running Inventory module on `localhost:5000`. Target: p99 under 150ms (Plan §8.2).
3. **CI total time** — query the GitHub API for the latest workflow run on the current branch and assert total duration under 10 minutes (Plan §293).

## Invocation

```sh
task gate -- 0
# or directly:
dotnet run --project tools/shopflow-gate -- 0
# or with JSON output:
dotnet run --project tools/shopflow-gate -- 0 --json
```

Expected output on a developer laptop today (pre-U12 sign-off, before Inventory + Aspire are runnable):

```
shopflow-gate phase 0
----------------------------------------
Skipped checks:
  - cold-start: skipped — needs the Aspire CLI on PATH (run `task setup` and `dotnet workload install aspire`)
  - auth-p99: skipped — Inventory not reachable at http://localhost:5000 (run `task up` first)
  - ci-time: skipped — needs GITHUB_TOKEN (or GH_TOKEN) and GITHUB_REPOSITORY env vars (CI sets these automatically; locally export them or run via `gh run`)
----------------------------------------
PASSED
```

Exit code 0. Skipped checks do **not** count as failures — the gate is informational pre-U12 sign-off, and U12 itself enforces real execution.

When everything is wired (Phase-0 sign-off, post-U12), expected output is:

```
shopflow-gate phase 0
----------------------------------------
Measurements:
  coldStartSeconds        47.20
  authP99Ms              118.40
  ciTotalMinutes           7.30
----------------------------------------
PASSED
```

## Today's caveats

| Check | When it runs today | When it will run for real |
|---|---|---|
| cold-start | When the Aspire CLI is on PATH (`dotnet workload install aspire`) | After U12 in CI; Phase-0 sign-off; every subsequent PR |
| auth-p99 | When Inventory is up at `localhost:5000` (post `task up`) | Once Inventory's auth controller exists (Phase 1 Sprint 1) |
| ci-time | When `GITHUB_TOKEN` (or `GH_TOKEN`) and `GITHUB_REPOSITORY` are set | Automatically in GitHub Actions; locally with `gh auth status` and an env-var export |

Each check that skips emits a structured "skipped — needs &lt;X&gt;" line that names the prerequisite. The gate keeps running; the next check still gets a chance.

## Adding a new phase gate

Phase 1+ implementations follow this recipe:

1. Add a class implementing `IPhaseGate` under `tools/shopflow-gate/Phases/`. Convention: file name + class name match the phase (e.g. `PhaseOneGate.cs`).
2. Wire the new gate into the `phase switch` in `Program.cs`.
3. Reuse `Chaos/MockChannelControlPlaneClient.cs` for any scenario injection. The client's `StartScenarioAsync` / `StopScenarioAsync` / `GetStateAsync` cover the entire control-plane API exposed by `infrastructure/mock-channels/_shared/controlPlane.js`.
4. Update this README with the new phase's checks + targets.
5. Update `Taskfile.yml` only if the invocation shape changes — `task gate -- N` already routes to any phase the CLI accepts.

The contract for a phase gate: never throw on expected pre-conditions (missing CLI tool, unreachable service, missing credentials). Those surface as `Skipped` entries, not exceptions. Skipped is informational; only `FailureReasons` flips the exit code to 1.

## See also

- `docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md` U11 — this CLI's home in the plan.
- `AGENTS.md` §3.16, §5.31, §6.37, §6.40 — the four analyzer rules whose Warning-to-Error promotion landed alongside this CLI.
- `infrastructure/mock-channels/README.md` — the chaos surface this CLI integrates with.
- `src/AppHost/ShopFlow.AppHost/Program.cs` — the dev orchestrator the cold-start check exercises.
