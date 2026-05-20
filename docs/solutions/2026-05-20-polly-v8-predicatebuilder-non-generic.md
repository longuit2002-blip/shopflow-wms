---
date: 2026-05-20
sprint: sprint-8.5
problem_type: build_error
severity: low
modules: [Channel, StockSync]
tags: [polly, resilience, predicatebuilder, api-drift, sprint-7.5-carry-over]
---

# Polly v8 `PredicateBuilder` — generic moves from method to type

## Problem

A Sprint-7.5-era test in `tests/ShopFlow.Channel.UnitTests/Adapters/ShopeeAdapterPushStockUpdateTests.cs` carried over from the Polly v7 → v8 migration with the wrong builder shape:

```csharp
// Compile error CS0308:
//   "The non-generic method 'PredicateBuilder<object>.HandleResult(Func<object, bool>)'
//    cannot be used with type arguments"
ShouldHandle = new PredicateBuilder()                    // v7 shape — non-generic builder
    .Handle<HttpRequestException>()
    .HandleResult<HttpResponseMessage>(r =>              // v7 placed the generic on the method
        (int)r.StatusCode >= 500
    ),
```

This compiled under Polly v7's `PredicateBuilder` (non-generic class with generic `HandleResult<TResult>` method). Polly v8 split the builder into two surfaces:

- `PredicateBuilder` — non-generic, used for handler chains that don't care about the result type
- `PredicateBuilder<TResult>` — generic, where `HandleResult` is non-generic (binds at type construction)

Sprint-3-redux U6 + Sprint-5 U5 introduced Polly v8 elsewhere with the correct shape, but this one test was left with the v7 syntax. The error stayed latent because `Channel.UnitTests` was not in `shopflow-migrate`'s transitive dep tree until Sprint-8 U10 forced a full-solution build.

## Fix

`PredicateBuilder<TResult>` only composes with the **typed** pipeline (`ResiliencePipelineBuilder<TResult>` + `RetryStrategyOptions<TResult>`). When the call site uses the **non-generic** `ResiliencePipelineBuilder` + `RetryStrategyOptions` (because the consumer ctor — here `ShopeeAdapter` — takes the non-generic `ResiliencePipeline`), the typed builder can't be assigned to the non-generic `ShouldHandle` slot.

The fix in that case is to hand-roll the predicate as a `Func<RetryPredicateArguments<object>, ValueTask<bool>>` against the `args.Outcome` discriminator:

```csharp
ShouldHandle = args => args.Outcome switch
{
    { Exception: HttpRequestException } => ValueTask.FromResult(true),
    { Result: HttpResponseMessage r } when (int)r.StatusCode >= 500
        => ValueTask.FromResult(true),
    _ => ValueTask.FromResult(false),
},
```

If the call site can be migrated to the typed pipeline, the cleaner shape uses the typed builder:

```csharp
// Inside ResiliencePipelineBuilder<HttpResponseMessage>().AddRetry(
//   new RetryStrategyOptions<HttpResponseMessage> { ... })
ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
    .Handle<HttpRequestException>()
    .HandleResult(r => (int)r.StatusCode >= 500),
```

Both shapes are equivalent — the result-type binding has just moved from per-method (v7) to either per-builder (typed pipeline) or per-call-site (lambda on the non-generic pipeline).

## Pattern (apply going forward)

When defining a Polly resilience pipeline that returns `TResult`:

- For pipelines that DO inspect the result (HTTP responses, custom result envelopes): `new PredicateBuilder<TResult>().Handle<TException>().HandleResult(r => ...)`
- For pipelines that only observe exceptions: `new PredicateBuilder().Handle<TException>().Handle<TOtherException>()`

The Polly v8 docs sometimes show both shapes side-by-side without flagging the migration boundary. If a `PredicateBuilder<T>.HandleResult<T>(...)` call surfaces a CS0308, the fix is always "move the generic up to the type construction site."

## Cross-references

- Sprint-3-redux U6 `MockShippingProvider` — Polly v8 `ResiliencePipelineBuilder` happy-path reference at `src/Services/Outbound/ShopFlow.Outbound.Infrastructure/Shipping/MockShippingProvider.cs`
- Sprint-5 U5 `PushPipelineFactory` — Polly v8 circuit-breaker + retry composition at `src/Services/StockSync/ShopFlow.StockSync.Infrastructure/Dispatch/PushPipelineFactory.cs`
- [Polly 8.x migration guide](https://www.pollydocs.org/migration-v8.html) — official upstream doc
