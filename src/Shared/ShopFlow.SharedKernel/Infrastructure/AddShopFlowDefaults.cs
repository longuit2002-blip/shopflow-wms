using FluentValidation;
using Hellang.Middleware.ProblemDetails;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Behaviors;
using HttpStatus = Microsoft.AspNetCore.Http.StatusCodes;

namespace ShopFlow.SharedKernel.Infrastructure;

/// <summary>
/// Composition-root extension that wires every cross-cutting concern in one
/// call: <c>services.AddShopFlowDefaults(configuration)</c>. Per AGENTS.md
/// §2.12 every module's <c>Program.cs</c> calls this; per-module wiring is
/// strictly additive (DbContext, repositories, module-specific consumers).
///
/// <para>What's wired (Phase-0-redux v3.0):</para>
/// <list type="bullet">
///   <item><description>OpenTelemetry traces + metrics with W3C TraceContext propagation</description></item>
///   <item><description>MediatR with the Logging, Tracing, and Validation pipeline behaviors</description></item>
///   <item><description>FluentValidation discovered from <paramref name="assembliesToScan"/></description></item>
///   <item><description>MassTransit (in-memory transport per ADR-0002 W1; W6 flips to RabbitMQ)</description></item>
///   <item><description>Hellang ProblemDetails (call <c>app.UseProblemDetails()</c> in the pipeline)</description></item>
///   <item><description><see cref="RequestContext"/> + <see cref="IRequestContext"/> alias scoped per request</description></item>
///   <item><description><see cref="PerRequestDbContextFactory{T}"/> registered as the open generic <see cref="IDbContextFactory{T}"/></description></item>
///   <item><description><see cref="OutboxInterceptor"/> scoped for DbContext wire-up</description></item>
/// </list>
/// <para>
/// Per ADR-0003 the v2.0 <c>TenancyInterceptor</c> is removed. Tenant
/// correctness is enforced by the routing middleware (call
/// <see cref="UseTenantRouting"/> on the <see cref="IApplicationBuilder"/>)
/// and the per-request DbContext factory; module DbContexts are constructed
/// only via that factory.
/// </para>
/// <para>
/// Serilog is intentionally NOT registered here — modules wire it via
/// <c>UseSerilog()</c> on the host builder so each deployment can pick its
/// own sinks without the kernel taking optional NuGet dependencies.
/// </para>
/// </summary>
public static class ShopFlowDefaultsExtensions
{
    /// <summary>
    /// Service name used when none is supplied via <see cref="ShopFlowDefaultsOptions.ServiceName"/>.
    /// Modules should override with their own (e.g. <c>"shopflow-inventory"</c>).
    /// </summary>
    public const string DefaultServiceName = "shopflow-service";

    /// <summary>
    /// Sprint-4 U4 (K13 close): register a CLR-type → <see cref="OutboxRoute"/>
    /// binding so <see cref="MultiplexedOutboxDispatcher{TContext}"/> sends
    /// (vs publishes) the matching outbox rows. Default destination name is
    /// kebab-case of the CLR type; pass an explicit <paramref name="destination"/>
    /// to override. Last-write-wins across module composition order.
    /// </summary>
    public static IServiceCollection AddOutboxRoute<TMessage>(
        this IServiceCollection services,
        SendKind kind,
        string? destination = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        // Ensure the registry singleton exists even if AddShopFlowDefaults
        // hasn't run yet (test composition order tolerance).
        services.TryAddSingleton<OutboxRouteRegistry>();
        services.TryAddSingleton<IOutboxRouteRegistry>(sp =>
            sp.GetRequiredService<OutboxRouteRegistry>()
        );

        // Seed the registry via DI enumeration — the registry's
        // constructor receives every registered OutboxRouteSeed at first
        // resolution and applies them last-write-wins.
        services.AddSingleton(
            new OutboxRouteSeed(typeof(TMessage), new OutboxRoute(kind, RoutingKey: destination))
        );

        return services;
    }

    public static IServiceCollection AddShopFlowDefaults(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ShopFlowDefaultsOptions>? configure = null,
        params System.Reflection.Assembly[] assembliesToScan
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new ShopFlowDefaultsOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(configuration);

        if (assembliesToScan.Length == 0)
        {
            assembliesToScan = new[] { System.Reflection.Assembly.GetCallingAssembly() };
        }

        // ---- Request context (per ADR-0003 — replaces TenancyInterceptor) ----
        services.AddScoped<RequestContext>();
        services.AddScoped<IRequestContext>(sp => sp.GetRequiredService<RequestContext>());

        // ---- Outbox route registry (Sprint-4 U4 / K13 close) ----
        // Singleton — populated by per-module AddOutboxRoute<T>(...) calls at
        // composition time, consumed by MultiplexedOutboxDispatcher per row.
        services.TryAddSingleton<OutboxRouteRegistry>();
        services.TryAddSingleton<IOutboxRouteRegistry>(sp =>
            sp.GetRequiredService<OutboxRouteRegistry>()
        );

        // ---- Per-request DbContext factory (open generic) -------------------
        // Resolves the connection string from IRequestContext at every Create()
        // call so each request hits the right tenant DB. ITenantCatalog itself
        // is registered by ControlPlane.Infrastructure (U5).
        services.AddScoped(typeof(IDbContextFactory<>), typeof(PerRequestDbContextFactory<>));

        // ---- EF Core interceptors ------------------------------------------
        services.TryAddScoped<OutboxInterceptor>();

        // ---- HttpContextAccessor (needed by routing middleware + JWT) ------
        services.AddHttpContextAccessor();

        // ---- OpenTelemetry --------------------------------------------------
        var serviceName = options.ServiceName ?? DefaultServiceName;
        services
            .AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(tp =>
            {
                tp.AddSource(TracingBehavior<object, object>.ActivitySourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter();
            })
            .WithMetrics(mp =>
            {
                mp.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddOtlpExporter();
            });

        // ---- MediatR + pipeline behaviors ----------------------------------
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(assembliesToScan);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(TracingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        // ---- FluentValidation ----------------------------------------------
        services.AddValidatorsFromAssemblies(assembliesToScan, includeInternalTypes: true);

        // ---- MassTransit (Sprint-2-redux U7 promotes RabbitMQ from W6 → W4) -
        // Transport selection precedence:
        //   1. configuration value "MessageBus:Transport" ("InMemory" or "RabbitMq")
        //   2. options.MessageBusTransport (defaults to RabbitMq)
        // Production reads the connection string from
        // configuration.GetConnectionString("rabbitmq") (Aspire injects as
        // ConnectionStrings__rabbitmq env var). Tests that don't need a real
        // broker pass MessageBusTransport=InMemory via the configure callback.
        // See docs/adr/0002-… postscript for the W6 → W4 rationale.
        var configuredTransport = configuration.GetValue<string>("MessageBus:Transport");
        var transport = !string.IsNullOrWhiteSpace(configuredTransport)
            ? Enum.Parse<MessageBusTransport>(configuredTransport, ignoreCase: true)
            : options.MessageBusTransport;

        services.AddMassTransit(bus =>
        {
            foreach (var asm in assembliesToScan)
            {
                bus.AddConsumers(asm);
                bus.AddSagaStateMachines(asm);
            }

            if (transport == MessageBusTransport.RabbitMq)
            {
                bus.UsingRabbitMq(
                    (context, cfg) =>
                    {
                        var rabbitConn = configuration.GetConnectionString("rabbitmq");
                        if (!string.IsNullOrWhiteSpace(rabbitConn))
                        {
                            cfg.Host(rabbitConn);
                        }
                        cfg.ConfigureEndpoints(context);
                    }
                );
            }
            else
            {
                bus.UsingInMemory(
                    (context, cfg) =>
                    {
                        cfg.ConfigureEndpoints(context);
                    }
                );
            }
        });

        // ---- ProblemDetails -------------------------------------------------
        Hellang.Middleware.ProblemDetails.ProblemDetailsExtensions.AddProblemDetails(
            services,
            setup =>
            {
                setup.IncludeExceptionDetails = (_, _) => options.IncludeExceptionDetails;
                setup.MapToStatusCode<FluentValidation.ValidationException>(
                    HttpStatus.Status400BadRequest
                );
                setup.MapToStatusCode<UnauthorizedAccessException>(
                    HttpStatus.Status401Unauthorized
                );
            }
        );

        return services;
    }

    /// <summary>
    /// Adds the <see cref="TenantRoutingMiddleware"/> to the request pipeline.
    /// Call this <em>before</em> any endpoint mapping so every handler runs
    /// inside a tenant-bound <see cref="RequestContext"/>. The middleware
    /// short-circuits with 400/403/404/503 on routing failures.
    /// </summary>
    public static IApplicationBuilder UseTenantRouting(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantRoutingMiddleware>();
}

/// <summary>
/// MassTransit transport selection per Sprint-2-redux plan R12.
/// </summary>
public enum MessageBusTransport
{
    InMemory = 0,
    RabbitMq = 1,
}

/// <summary>
/// Knobs the consumer can tune when calling
/// <see cref="ShopFlowDefaultsExtensions.AddShopFlowDefaults"/>.
/// </summary>
public sealed class ShopFlowDefaultsOptions
{
    /// <summary>
    /// OpenTelemetry resource service name. Defaults to
    /// <c>"shopflow-service"</c>; modules should set their own (e.g.
    /// <c>"shopflow-inventory"</c>).
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// When true, ProblemDetails responses include exception stack traces.
    /// Intended for development environments only.
    /// </summary>
    public bool IncludeExceptionDetails { get; set; }

    /// <summary>
    /// MassTransit transport selection per Sprint-2-redux plan R12.
    /// Default <see cref="MessageBusTransport.RabbitMq"/>. Tests that
    /// don't need a real broker should set this to
    /// <see cref="MessageBusTransport.InMemory"/>. Configuration value
    /// <c>MessageBus:Transport</c> (when present) overrides this.
    /// </summary>
    public MessageBusTransport MessageBusTransport { get; set; } = MessageBusTransport.RabbitMq;
}
