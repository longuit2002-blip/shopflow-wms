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

        // U4 — FulfillmentSaga state machine + MT EF saga repository + the
        // SignalR relay consumers are configured inside the kernel's SINGLE
        // AddMassTransit via the ShopFlowDefaultsOptions.ConfigureBus hook
        // (Outbound.Api/Program.cs passes ConfigureOutboundBus below).
        // MassTransit forbids a second AddMassTransit() per container — a
        // second call here is exactly what kept the Outbound.Api host from
        // building (finish-line U4; see
        // docs/solutions/2026-05-27-outbound-api-never-booted-composition-bugs.md).

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

    /// <summary>
    /// Finish-line U4 — the Outbound-specific bus configuration that MUST run
    /// inside the kernel's SINGLE <c>AddMassTransit</c> call. MassTransit forbids
    /// a second <c>AddMassTransit</c> per container, so <c>Outbound.Api/Program.cs</c>
    /// passes this method as <see cref="ShopFlowDefaultsOptions.ConfigureBus"/>.
    /// It gives the <see cref="FulfillmentSaga"/> its EF saga repository (against
    /// <c>saga_state</c>) and registers the two SignalR relay consumers, which
    /// live in <c>ShopFlow.SharedKernel.Infrastructure.SignalR</c> — an assembly
    /// the kernel does NOT scan, so they would otherwise never register.
    /// </summary>
    /// <remarks>
    /// <para>The kernel's <c>AddSagaStateMachines(asm)</c> scan also discovers
    /// <see cref="FulfillmentSaga"/> in the scanned Application assembly;
    /// re-registering it here returns the same registration and the explicit
    /// <c>EntityFrameworkRepository</c> configuration takes effect. The repository
    /// resolves the scoped <see cref="OutboundDbContext"/> registered in
    /// <see cref="AddOutboundModule"/>, which reads
    /// <c>IRequestContext.DbConnectionString</c> at construction — so the saga
    /// lands in the correct per-tenant DB once the tenant is bound for the
    /// consume scope.</para>
    ///
    /// <para>The relays register ONLY on Outbound.Api (the single hub-host per
    /// the Sprint-7 decision): if they subscribed in the kernel — which runs for
    /// every module API — every module process would join the same pub/sub
    /// topology and competing-consumer semantics would deliver each event to one
    /// arbitrary process, not necessarily the hub host the client is connected
    /// to. Registering them here keeps the relay process == the hub-host
    /// process.</para>
    /// </remarks>
    public static void ConfigureOutboundBus(IBusRegistrationConfigurator bus)
    {
        ArgumentNullException.ThrowIfNull(bus);

        bus.AddSagaStateMachine<FulfillmentSaga, FulfillmentSagaState>()
            .EntityFrameworkRepository(r =>
            {
                r.ExistingDbContext<OutboundDbContext>();
                // Postgres-specific row-lock statement for the saga repository's
                // pessimistic concurrency (R5).
                r.UsePostgres();
            });

        bus.AddConsumer<ShopFlow.SharedKernel.Infrastructure.SignalR.StockChangedRelayConsumer>();
        bus.AddConsumer<ShopFlow.SharedKernel.Infrastructure.SignalR.SagaTransitionedRelayConsumer>();
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
