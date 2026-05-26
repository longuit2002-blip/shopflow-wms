using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Retry;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Application.Sagas;
using ShopFlow.Outbound.Infrastructure.Outbox;
using ShopFlow.Outbound.Infrastructure.Persistence;
using ShopFlow.Outbound.Infrastructure.Repositories;
using ShopFlow.Outbound.Infrastructure.Sagas;
using ShopFlow.Outbound.Infrastructure.Shipping;
using ShopFlow.Outbound.Infrastructure.Workers;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Outbound.Infrastructure;

/// <summary>
/// Composition root for the Outbound module per AGENTS.md §11.79. The
/// Api project's <c>Program.cs</c> calls
/// <c>services.AddShopFlowDefaults(...)</c> first (kernel concerns) and
/// then <c>services.AddOutboundModule(configuration)</c> from here. U1
/// shipped the scaffold + DbContext registration + outbox dispatcher
/// hosted service; U2 wires the Order repository + UnitOfWork +
/// IOutboundOutbox; saga + pick queue + mock carrier land in U4/U5/U6.
/// </summary>
public static class OutboundServiceCollectionExtensions
{
    public const string ModuleName = "Outbound";

    public static IServiceCollection AddOutboundModule(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Sprint-7.5 U8 — the SagaTransitionDuplicateInterceptor pre-checks
        // OrderTransition adds against the new composite UNIQUE on
        // outbound_saga_transitions and detaches duplicates so MT-redelivered
        // consume scopes never trigger the 23505. Scoped lifetime so the
        // logger resolves against the same scope as the DbContext.
        services.AddScoped<SagaTransitionDuplicateInterceptor>();

        services.AddScoped<OutboundDbContext>(sp =>
        {
            var ctx = sp.GetRequiredService<IRequestContext>();
            var dupeInterceptor = sp.GetRequiredService<SagaTransitionDuplicateInterceptor>();
            var options = new DbContextOptionsBuilder<OutboundDbContext>()
                .UseNpgsql(
                    ctx.DbConnectionString,
                    npg =>
                        npg.MigrationsAssembly(
                            typeof(OutboundServiceCollectionExtensions).Assembly.GetName().Name
                        )
                )
                .AddInterceptors(dupeInterceptor)
                .Options;
            return new OutboundDbContext(options);
        });

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IUnitOfWork, OutboundUnitOfWork>();
        services.AddScoped<IOutboundOutbox, OutboundOutbox>();

        // Sprint-7 U1 — append-only saga-transitions audit surface.
        // Scoped so it shares the per-tenant OutboundDbContext registered
        // above; the saga's IStateObserver (U2) resolves this repository
        // from the consume scope's IServiceProvider so the audit write
        // commits atomically with the saga state row.
        services.AddScoped<IOrderTransitionRepository, OrderTransitionRepository>();

        // Sprint-7 U2 — SagaTransitionObserver writes one audit row +
        // appends one SagaTransitionedV1 to the outbox per state transition.
        // Scoped lifetime so it shares the consume-scope DbContext + outbox
        // surface with the saga's MT EF repository commit (co-transactional).
        // FulfillmentSaga's static RecordTransitionAsync helper resolves
        // this via ctx.GetPayload<IServiceProvider>() at every TransitionTo
        // site (including WhenEnter IfElse branches + If counter-drain
        // branch per the Sprint-7 doc-review IStateObserver decision).
        services.AddScoped<Application.Sagas.SagaTransitionObserver>();

        // Sprint-7 U2 — route SagaTransitionedV1 outbox rows through the
        // multiplexed dispatcher as a Publish (broadcast). The relay
        // consumer (Sprint-7 U6) subscribes from a queue per the standard
        // MT pub/sub binding and pushes to the tenant-scoped SignalR group.
        services.AddOutboxRoute<ShopFlow.Contracts.Outbound.SagaTransitionedV1>(SendKind.Publish);

        // U5 — pick wave generator dependencies. PickQueue is Singleton
        // so the per-tenant Channel registry survives across consume
        // scopes (saga writers + generator reader share the same
        // channels). PickWaveRepository + PickerRepository are Scoped
        // because they read/write through the per-tenant OutboundDbContext.
        services.AddSingleton<IPickQueue, PickQueue.PickQueue>();
        services.AddScoped<IPickWaveRepository, PickWaveRepository>();
        services.AddScoped<IPickerRepository, PickerRepository>();

        // U4 — FulfillmentSaga state machine + MT EF saga repository
        // against saga_state. The saga itself is registered here so the
        // bus-level AddMassTransit() in AddShopFlowDefaults can resolve
        // it via DI. The EntityFrameworkRepository pattern uses MT's
        // .ExistingDbContext<OutboundDbContext>() which resolves the
        // scoped OutboundDbContext registered above — that DbContext
        // reads IRequestContext.DbConnectionString at construction, so
        // when TenantBindingSagaFilter (K12 primary path) binds the
        // tenant BEFORE the saga repo runs, the DbContext lands in the
        // correct per-tenant DB.
        services.AddMassTransit(bus =>
        {
            bus.AddSagaStateMachine<FulfillmentSaga, FulfillmentSagaState>()
                .EntityFrameworkRepository(r =>
                {
                    r.ExistingDbContext<OutboundDbContext>();
                    // Postgres-specific row-lock statement for the saga
                    // repository's pessimistic concurrency (R5).
                    r.UsePostgres();
                });

            // Sprint-7 U6 — SignalR relay consumers register HERE (inside
            // the Outbound MassTransit block) rather than in
            // AddShopFlowDefaults. Reason: AddShopFlowDefaults runs for
            // every module API (Inventory / StockSync / Channel / Inbound /
            // Auth); if the relays subscribed there, every module process
            // would join the same RabbitMQ pub/sub topology and the
            // competing-consumer semantics would deliver each event to
            // only ONE process per round-robin. The connected SignalR
            // client lives on whichever process the Gateway routes /hub
            // to (Outbound.Api per the SINGLE-HUB-HOST decision), so a
            // miss-routed delivery would silently drop the event.
            // Registering only here keeps the relay process == the hub
            // host process, eliminating the race.
            bus.AddConsumer<ShopFlow.SharedKernel.Infrastructure.SignalR.StockChangedRelayConsumer>();
            bus.AddConsumer<ShopFlow.SharedKernel.Infrastructure.SignalR.SagaTransitionedRelayConsumer>();
        });

        // U4 K12 (primary path) — the open-generic filter is registered
        // here as Scoped so MT's pipe-builder can resolve it per message.
        // The bus-level wiring that attaches the filter to receive
        // endpoints lives in the Api project's Program.cs (alongside
        // AddShopFlowDefaults), which has access to the MassTransit
        // bus configurator. U4's tests configure the filter directly
        // on the in-test bus configurator.
        services.AddScoped(typeof(TenantBindingSagaFilter<>));

        // K12 fallback path — kept registered as Singleton so the
        // factory can be swapped into the saga repo's .DatabaseFactory()
        // pipeline if the filter path needs replacement. NOT currently
        // wired into the repo — see TenantAwareSagaDbContextFactory's
        // docs for the swap procedure.
        services.AddSingleton<TenantAwareSagaDbContextFactory>();

        services.TryAddSingleton<TimeProvider>(TimeProvider.System);

        services.AddHostedService<MultiplexedOutboxDispatcher<OutboundDbContext>>();

        // U5 — pick wave generator hosted service. Single-instance per
        // Phase-1 modular monolith host; Phase-2 multi-instance leader
        // election is tracked in the plan's risk row.
        services.AddHostedService<PickWaveGeneratorService>();

        // U6 — IMockShippingProvider Singleton + Polly v8 ResiliencePipeline.
        // Build the pipeline once at composition time; the pipeline itself
        // is thread-safe so a single instance is shared across all carrier
        // calls. Strategy: 3 retries on TransientShippingException with
        // 200 ms constant backoff (per plan U6 Approach + K5). Polly v8's
        // ResiliencePipelineBuilder is the canonical replacement for v7's
        // Policy.Handle<T>().WaitAndRetryAsync(...) DSL.
        services.AddSingleton<ResiliencePipeline>(_ =>
            new ResiliencePipelineBuilder()
                .AddRetry(
                    new RetryStrategyOptions
                    {
                        MaxRetryAttempts = 3,
                        Delay = TimeSpan.FromMilliseconds(200),
                        BackoffType = DelayBackoffType.Constant,
                        ShouldHandle = new PredicateBuilder().Handle<TransientShippingException>(),
                    }
                )
                .Build()
        );
        services.AddSingleton<IMockShippingProvider>(sp => new MockShippingProvider(
            sp.GetRequiredService<ResiliencePipeline>()
        ));

        // U6 — ChannelTrackingConsumer auto-registered via AddConsumers(asm)
        // in the kernel-wide AddShopFlowDefaults MassTransit configuration
        // (the Infrastructure assembly is one of the scanned assemblies).
        // No explicit registration needed; Phase-2 Sprint-4 relocates the
        // consumer to ShopFlow.Channel.Infrastructure with a real adapter.

        return services;
    }
}

file static class ServiceCollectionExtensions
{
    /// <summary>
    /// Mirrors the Inbound / Inventory pattern: only register the instance
    /// if no existing registration is present. The OOTB
    /// <c>Microsoft.Extensions.DependencyInjection.Extensions.TryAddSingleton</c>
    /// takes an implementation type, not an instance.
    /// </summary>
    public static void TryAddSingleton<TService>(
        this IServiceCollection services,
        TService instance
    )
        where TService : class
    {
        if (!services.Any(d => d.ServiceType == typeof(TService)))
        {
            services.AddSingleton(instance);
        }
    }
}
