using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Application.Sagas;
using ShopFlow.Outbound.Infrastructure.Outbox;
using ShopFlow.Outbound.Infrastructure.Repositories;
using ShopFlow.Outbound.Infrastructure.Sagas;
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

        services.AddScoped<OutboundDbContext>(sp =>
        {
            var ctx = sp.GetRequiredService<IRequestContext>();
            var options = new DbContextOptionsBuilder<OutboundDbContext>()
                .UseNpgsql(
                    ctx.DbConnectionString,
                    npg =>
                        npg.MigrationsAssembly(
                            typeof(OutboundServiceCollectionExtensions).Assembly.GetName().Name
                        )
                )
                .Options;
            return new OutboundDbContext(options);
        });

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IUnitOfWork, OutboundUnitOfWork>();
        services.AddScoped<IOutboundOutbox, OutboundOutbox>();

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
