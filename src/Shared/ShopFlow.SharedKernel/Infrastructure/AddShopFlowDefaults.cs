using FluentValidation;
using Hellang.Middleware.ProblemDetails;
using MassTransit;
using MediatR;
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
/// <para>What's wired:</para>
/// <list type="bullet">
///   <item><description>OpenTelemetry traces + metrics with W3C TraceContext propagation</description></item>
///   <item><description>MediatR with the Logging, Tracing, and Validation pipeline behaviors</description></item>
///   <item><description>FluentValidation, discovered from <paramref name="assembliesToScan"/></description></item>
///   <item><description>MassTransit (in-memory transport per ADR-0002 W1; W6 flips to RabbitMQ)</description></item>
///   <item><description>Hellang ProblemDetails (call <c>app.UseProblemDetails()</c> in the pipeline)</description></item>
///   <item><description><see cref="IRequestContext"/> as a scoped <see cref="RequestContext"/></description></item>
/// </list>
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

        // Surface the configuration to the DI container so consumers can
        // resolve IConfiguration from any kernel-registered service. Any
        // section-bound options registration lives downstream — keeping the
        // kernel free of Microsoft.Extensions.Configuration.Binder so it
        // composes against the lightest possible dependency surface.
        services.TryAddSingleton(configuration);

        if (assembliesToScan.Length == 0)
        {
            assembliesToScan = new[] { System.Reflection.Assembly.GetCallingAssembly() };
        }

        // ---- Request context ------------------------------------------------
        services.AddScoped<RequestContext>();
        services.AddScoped<IRequestContext>(sp => sp.GetRequiredService<RequestContext>());

        // ---- Serilog --------------------------------------------------------
        // Per-module Program.cs is responsible for configuring the Serilog
        // pipeline (sinks, enrichers) via UseSerilog() on the host builder;
        // the kernel intentionally does NOT take dependencies on optional
        // sink packages (Console, Seq, OTLP) because those are deployment-
        // shaped choices owned by each host. The Serilog.AspNetCore package
        // referenced in this csproj only provides the request-logging
        // middleware — the host wires it.
        //
        // (No registration is needed here; Serilog's static Log.Logger plus
        // UseSerilog() in Program.cs is the canonical wire-up.)

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

        // ---- MassTransit (W1: in-memory; W6: RabbitMQ via ADR-0002) --------
        services.AddMassTransit(bus =>
        {
            foreach (var asm in assembliesToScan)
            {
                bus.AddConsumers(asm);
                bus.AddSagaStateMachines(asm);
            }
            bus.UsingInMemory(
                (context, cfg) =>
                {
                    cfg.ConfigureEndpoints(context);
                }
            );
        });

        // ---- ProblemDetails -------------------------------------------------
        // Hellang's AddProblemDetails clashes by name with the built-in
        // Microsoft.AspNetCore.Http extension; call the Hellang overload
        // explicitly so the resolution is unambiguous regardless of which
        // namespaces the consumer imports.
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

        // ---- EF Core interceptors ------------------------------------------
        services.TryAddScoped<TenancyInterceptor>();
        services.TryAddScoped<OutboxInterceptor>();

        return services;
    }
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
}
