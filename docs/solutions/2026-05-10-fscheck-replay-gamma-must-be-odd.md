# FsCheck `Replay = "(seed,gamma)"` — gamma must be odd, or all properties die silently

**Date**: 2026-05-10
**Affects**: any test project using `FsCheck.Xunit` with a pinned `Replay` argument

## Problem

A FsCheck.Xunit test project compiled cleanly, all 5 properties showed up in `dotnet test --list-tests`, but `dotnet test` reported `Total: 1, Passed: 1` — only the unrelated smoke test ran. No property failure messages, no skip messages, no test count for the properties. The properties were silently dropped.

The first user-visible signal was `dotnet test --filter "Category!=Integration&Category!=Load"` aggregating only 62 tests after U8 — exactly the pre-U8 number, even though `dotnet build` reported `ShopFlow.PropertyTests` compiled fine and `--list-tests` listed 5 new entries.

## Root cause

`dotnet test --logger:"console;verbosity=normal"` surfaced an unhandled exception thrown DURING the FsCheck.Xunit test executor's setup phase, BEFORE any property method was invoked:

```
Unhandled exception. System.ArgumentException: Gamma must be odd, given: 4242 (Parameter 'gamma')
   at FsCheck.Rnd..ctor(UInt64 seed, UInt64 gamma)
   at FsCheck.Xunit.PropertyConfigModule.parseReplay(String str)
```

FsCheck's `Replay` attribute argument has the format `"(seed,gamma)"` where both values are `UInt64`. The library enforces a hard invariant: **gamma must be odd**. The original pin was `"(42,4242)"`. 4242 is even. `FsCheck.Rnd..ctor` throws `ArgumentException`. xUnit's runner catches the exception at the test-class level, dropping every property test in the class without registering them as failures (because they never *started*).

This is exactly the kind of "compiles fine, runs nothing, fails silently" failure mode the test-first discipline (AGENTS.md §8) is supposed to prevent. The CI gate would have caught it because the property count would be wrong, but a developer running `task test` quickly sees `Passed!` and moves on without realizing 5 properties were skipped.

## Solution

Use an odd gamma. The pin is now `"(42,4243)"`. Documented in `tests/ShopFlow.PropertyTests/ReservationLedgerProperties.cs` next to the constant, and re-stated in the property attribute comments where future readers see them.

## Prevention

1. **Choose Replay seeds that are explicitly odd in the gamma slot.** Easy convention: pin the gamma to a known odd number (the next-prime to your project's lucky number works fine — or just any number ending in 1/3/5/7/9).
2. **Always run the property tests, don't trust the count.** A test count regressing without an explicit deletion should be treated as a failure. U9's CI workflow asserts on the property count via `dotnet test --filter "Category!=Integration&Category!=Load" -- --report-trx-filename ...` and fails if the count is below the expected baseline. (Bracket-baseline guards exactly this class of silent-skip failure.)
3. **Pin gammas in `Directory.Build.props` for tests, not per-file.** Future TODO: hoist `PinnedReplay` to a shared test-time constant so all property suites use the same vetted seed. Out of scope for this entry.

## How to diagnose this class of failure quickly

If `dotnet test --list-tests` shows tests that don't run:

```sh
dotnet test <project> --no-build --logger:"console;verbosity=normal"
```

The unhandled exception will be at the **top** of the output, before xUnit's "Passed!" summary. Default verbosity hides it.

## References

- `tests/ShopFlow.PropertyTests/ReservationLedgerProperties.cs` — `PinnedReplay` constant
- FsCheck source: `FsCheck.Rnd` constructor in `Random.fs`
- AGENTS.md §8.54 — pinned random seeds discipline
