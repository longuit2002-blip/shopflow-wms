---
date: 2026-05-20
sprint: sprint-8.5
problem_type: build_error
severity: low
modules: [Outbound, shared]
tags: [timeprovider, fake-time, dotnet-9, cpm, sprint-7-carry-over]
---

# `Microsoft.Extensions.TimeProvider.Testing` — canonical .NET 9 FakeTimeProvider package

## Problem

Sprint-7 introduced `SagaTransitionObserver` + observer tests at `tests/ShopFlow.Outbound.UnitTests/Sagas/SagaTransitionObserverTests.cs`. The tests use `FakeTimeProvider` from `Microsoft.Extensions.Time.Testing` for deterministic timestamp control. The csproj never gained the corresponding `PackageReference`, so the namespace fails to resolve:

```
CS0234: The type or namespace name 'Time' does not exist in the namespace 'Microsoft.Extensions'
```

The error stayed latent because `Outbound.UnitTests` wasn't in `shopflow-migrate`'s transitive dep chain until Sprint-8 U10's full-solution build surfaced it.

## Package choice rationale

The `.NET 9` ecosystem offers two ways to control time in tests:

1. **`Microsoft.Extensions.TimeProvider.Testing`** — Microsoft's first-party `FakeTimeProvider`. Implements `System.TimeProvider` so any code using the abstraction can take it via DI / constructor injection. Time advances via `Advance(TimeSpan)` and `SetUtcNow(DateTimeOffset)`.

2. **Hand-rolled `TimeProvider` subclass + Moq/NSubstitute** — possible but loses the structural benefits (every consumer of `TimeProvider.System` needs explicit injection wiring; FakeTimeProvider gives a drop-in replacement registered as a singleton).

Sprint-8.5 picks **(1)** — the canonical Microsoft package. Reasons:

- Pure-managed first-party impl; no Moq dependency.
- `Advance(TimeSpan)` is the test idiom every TimeProvider-aware codebase converges on; consistent with reading any other test suite.
- Sprint-9+ MFA / TOTP work will need similar deterministic time control; CPM pin makes adoption one-line.
- Pinned at `9.0.0` to match the EF Core 9 / AspNetCore 9 stack already at 9.0.x. (`10.0.7` is also available; held to 9.0.0 because Outbound.UnitTests doesn't currently pull Hosting 10.0.7 transitively. U10 Logging bump to 10.0.7 is independent.)

## Fix

`Directory.Packages.props`:
```xml
<ItemGroup Label="Test infrastructure — Sprint-8.5 sweep">
    <PackageVersion Include="Microsoft.Extensions.TimeProvider.Testing" Version="9.0.0" />
</ItemGroup>
```

`tests/ShopFlow.Outbound.UnitTests/ShopFlow.Outbound.UnitTests.csproj`:
```xml
<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
```

Existing test code at `SagaTransitionObserverTests.cs(5,28)` resolves once the package surfaces:
```csharp
using Microsoft.Extensions.Time.Testing;  // now resolves
// ...
var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
clock.Advance(TimeSpan.FromMinutes(5));
```

## Pattern (apply going forward)

When introducing time-dependent code:

- Inject `TimeProvider` (the abstraction in `System`) at the consumer; default to `TimeProvider.System` in non-test composition.
- In tests, register `new FakeTimeProvider(initialMoment)` via `Options.Create` / DI and advance with `clock.Advance(TimeSpan)`.
- Avoid `DateTime.UtcNow` / `DateTimeOffset.UtcNow` at call sites — they're untestable. Move every use through the injected `TimeProvider`.

The `TimeProvider.System` singleton in `Microsoft.Extensions.Hosting.Services.AddShopFlowDefaults` (Sprint-5 U7 K7 precedent) is already the canonical wiring; consumers just need to take `TimeProvider` instead of building their own clock surface.

## Cross-references

- Sprint-5 U7 `CachingSkuFlagRepository` — `TimeProvider` injection pattern at `src/Services/StockSync/ShopFlow.StockSync.Infrastructure/Repositories/CachingSkuFlagRepository.cs`
- Sprint-7 `SagaTransitionObserver` — the consumer this test exercises at `src/Services/Outbound/ShopFlow.Outbound.Application/Sagas/SagaTransitionObserver.cs`
- [Microsoft.Extensions.TimeProvider.Testing on NuGet](https://www.nuget.org/packages/Microsoft.Extensions.TimeProvider.Testing/)
