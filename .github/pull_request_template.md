<!--
  Conventional commits + plan-unit citation. See AGENTS.md §10 for the
  full rule canon (commit + PR hygiene). Title format:
    <type>(<scope>): <subject>
  where <type> is one of: feat, fix, docs, refactor, test, chore, ci, build.
-->

## Summary

<!-- 1-3 sentences. Lead with the why, not the what. -->

## Closes

<!-- Cite the U-IDs from the active plan in docs/plans/. Example:
     Closes U9 of docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md
-->

## Verification

<!-- How did you verify this works? CI gates run automatically; list
     anything you exercised locally that CI does not (e.g., cold-start
     timing, manual smoke against the dev orchestrator). -->

- [ ] `task ci` passes locally
- [ ] CSharpier check is clean (`dotnet csharpier --check .`)
- [ ] No new XML comments contain double-dashes (see docs/solutions/2026-04-28-csproj-xml-comment-double-dash.md)
- [ ] AGENTS.md updated if a new rule emerged (per AGENTS.md §9.61)
- [ ] docs/solutions/ entry added if any fix took >5 min to diagnose

## Notes for reviewers

<!-- Anything load-bearing that is not obvious from the diff. -->
