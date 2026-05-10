# Green-against-stub property/load suites: red-for-the-right-reason without flaky CI

## Problem

U8 ships FsCheck property tests + NBomber scenarios *before* the
implementations they assert against (Phase-1 Sprint-1 reservation ledger
in W3, Phase-2 Sprint-5 sync engine in W7). The plain "test-first" choice
— properties fail with `NotImplementedException` — would block the U9 CI
gate from going live: every PR would have a red property job for
3+ weeks, and reviewers would learn to ignore the red bar (the "green
shield syndrome" inversion).

The opposite extreme — `[Skip]` on every property until the impl arrives
— hides the spec entirely; the moment the impl lands nobody remembers
to un-skip, and the suite is dead.

## Root cause

The dichotomy was false. The property suite is testing two things at
once: (a) the spec's invariants, and (b) the *type* of failure when the
seam is unimplemented. In W1 only (b) is meaningful; in W3+ only (a)
matters. The same test code can carry both if the dispatch is on
exception type, not on a `[Skip]` attribute.

## Solution

Each property / load scenario routes its repository / primitive calls
through a small `ExpectStubFailureOrAssert(realAssertion, stubPrefix)`
helper:

```csharp
private static async Task<bool> ExpectStubFailureOrAssert(
    Func<Task> realAssertion,
    string stubMessagePrefix)
{
    try { await realAssertion(); return true; }
    catch (NotImplementedException ex)
        when (ex.Message.StartsWith(stubMessagePrefix, StringComparison.Ordinal))
    {
        // Expected-stub-state in W1; flips to live assertion at impl time.
        return true;
    }
}
```

Stubs throw `NotImplementedException` with a known prefix
(`"ReservationRepository stub — Phase-1 Sprint-1 (W3) lands this..."`).
The helper catches *only* that exact prefix; any other exception (or a
real impl that doesn't satisfy the invariant) bubbles up as a property
failure.

The pivot is automatic: when Phase-1 Sprint-1 replaces the stub with a
real `ReservationRepository`, the catch branch never fires and the
`realAssertion` body — already written — becomes the live invariant
check. No test edits required.

## Prevention

- The pattern is documented in
  `tests/ShopFlow.PropertyTests/ReservationLedgerProperties.cs` class-
  level summary and in each `Stubs/NotImplemented*.cs` header. The
  stub message prefix is a `public const string` so the matching
  predicate stays type-safe across the seam.
- The `when` clause matches on the message *prefix*, not the full
  message. Each method's exception carries a method-specific suffix
  (`"... TryReserveAsync — see Tech Design §7.2"`) so a real-impl
  partial implementation that throws NIE from one method but works in
  another shows up as a real failure in the working method's property,
  not as a silent green.
- AGENTS.md §8.55 already encodes "the harness IS the spec"; this doc
  is the implementation-level companion to that rule.

## References

- `01-product-development-plan.md.docx` §299 (reservation invariants)
- `01-product-development-plan.md.docx` §316–§323 (sync primitives)
- `docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md`
  U8, esp. the "Configuration choice: instead of `--filter` excluding red
  tests, the property suite asserts on the *type* of failure" passage
  (Verification, line 631).
- AGENTS.md §8.54 (FsCheck pinned seeds), §8.55 (test-first stubs),
  §8.59 (load tests are nightly).
