# Solutions / Learnings Compounding

Short, atomic notes on problems that bit us once and the discipline that prevents the second hit. The compound-engineering principle: "every reviewer comment on an AI-assisted PR is a missing rule" (root [`AGENTS.md`](../../AGENTS.md) §9.61). When AGENTS.md isn't the right home (because the rule is too narrow / too situational), the learning lands here.

## Convention

- **Filename**: `YYYY-MM-DD-kebab-case-topic.md` (date is when the issue was diagnosed, not when it first occurred)
- **Length**: target 40–80 lines. Atomic. One problem per file.
- **Sections**: Problem → Root cause → Solution → Prevention → References.
- **Triggering rule**: write an entry whenever a fix took longer than ~5 minutes to diagnose, OR was non-obvious from reading the project tree alone, OR is the kind of thing an AI agent would re-discover from scratch each time. Re-discovery is exactly what compounding prevents.

## When to consult

- Before reaching for a stack trace or web search, scan filenames here. Three minutes here can save 30 minutes of debugging.
- Before bumping a package, dropping a tool, or refactoring scaffolding, scan for entries that mention it.
- Future-you in a fresh `ce-work` session will see this directory automatically (it sits next to the plan and ADR docs in `docs/`).

## Index

| File | Topic |
|---|---|
| [2026-04-28-csharpier-cli-syntax.md](2026-04-28-csharpier-cli-syntax.md) | CSharpier 0.30.x uses `--check` flag, not `check` subcommand |
| [2026-04-28-csproj-xml-comment-double-dash.md](2026-04-28-csproj-xml-comment-double-dash.md) | XML comments cannot contain `--` (forbids `--check`, `--filter`, etc.) |
| [2026-04-28-husky-net-path-discovery.md](2026-04-28-husky-net-path-discovery.md) | Pre-commit hook fails because shell PATH is stale after winget install |
| [2026-04-28-central-package-management.md](2026-04-28-central-package-management.md) | Why CPM + Directory.Build.props from day 1 — version drift + transitive resolution |
| [2026-04-28-test-csproj-conventions.md](2026-04-28-test-csproj-conventions.md) | xUnit implicit usings, NU1701 NoWarn, IActionResult assembly reference |
| [2026-05-10-mock-channel-shared-library-pattern.md](2026-05-10-mock-channel-shared-library-pattern.md) | Mock channels: `_shared/` carries everything that isn't marketplace-specific (signing + endpoints + webhook headers) |
| [2026-05-10-green-against-stub-property-suite.md](2026-05-10-green-against-stub-property-suite.md) | Property/load suites: catch `NotImplementedException` with a known prefix → green-against-stub in W1, live invariant in W3+ without test edits |
| [2026-05-10-fscheck-replay-gamma-must-be-odd.md](2026-05-10-fscheck-replay-gamma-must-be-odd.md) | FsCheck `Replay = "(seed,gamma)"` — gamma must be odd, or every property silently dies before running |
| [2026-05-10-aspire-adddockerfile-context-path.md](2026-05-10-aspire-adddockerfile-context-path.md) | Aspire `AddDockerfile` resolves `contextPath` against the AppHost csproj, not the repo root |
| [2026-05-10-aspire-resource-name-rules.md](2026-05-10-aspire-resource-name-rules.md) | Aspire ASPIRE006: resource names must be ASCII letters/digits/hyphens — no underscores. Distinct from underlying DB/queue names which may keep underscores. |
