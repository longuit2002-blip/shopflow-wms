using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentValidation;
using Hellang.Middleware.ProblemDetails;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Behaviors;
using ShopFlow.SharedKernel.Authorization;
using ShopFlow.SharedKernel.Infrastructure.SignalR;
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

            // Finish-line U4 — per-module bus configuration hook. MassTransit
            // forbids more than one AddMassTransit() per container, so a module
            // that needs to configure a saga repository, attach extra consumers
            // not in the scanned assemblies (e.g. the SignalR relays), or add a
            // bus-level filter MUST do it inside this single call rather than a
            // second AddMassTransit. Outbound.Api uses this to give the
            // FulfillmentSaga its EntityFrameworkRepository + register the relay
            // consumers; previously AddOutboundModule called a SECOND
            // AddMassTransit, which threw ConfigurationException and kept the
            // Outbound.Api host from ever building. See
            // docs/solutions/2026-05-27-outbound-api-never-booted-composition-bugs.md.
            options.ConfigureBus?.Invoke(bus);

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

        // ---- JwtBearer authentication (Sprint-7 U5: lifted from Inventory.Api) -
        // Every module's API goes through the same dev-secret HMAC verification
        // that Auth.Api signs with. Closes Sprint-6 trade-off #8 — JWT bearer
        // registration was duplicated in Inventory.Api; lifting it here makes
        // it kernel-wide so Outbound.Api, Channel.Api, Inbound.Api, StockSync.Api
        // all inherit the same scheme via AddShopFlowDefaults. Sprint-7 still
        // uses the dev secret; Sprint-8 replaces with a real signer.
        //
        // The OnMessageReceived handler is the SignalR-specific bit: the
        // browser SignalR client cannot set Authorization headers on the
        // WebSocket upgrade (HTML spec restriction), so it falls back to
        // ?access_token=... query string. We copy that into context.Token
        // and IMMEDIATELY strip the parameter from the URL so request
        // logging middleware does NOT leak the bearer credential to access
        // logs (doc-review SEC-001 mitigation).
        var devSecret =
            configuration["Auth:DevSecret"]
            ?? throw new InvalidOperationException(
                "Auth:DevSecret missing. AddShopFlowDefaults requires a shared "
                    + "secret with Auth.Api so each module API can validate JWTs "
                    + "issued by the auth module. Sprint-7 swaps for a shared signer."
            );
        var issuer = configuration["Auth:Issuer"] ?? "shopflow-dev";
        var audience = configuration["Auth:Audience"] ?? "shopflow-api";

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(opts =>
            {
                opts.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(devSecret)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };

                opts.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var path = ctx.HttpContext.Request.Path;
                        if (!path.StartsWithSegments(SignalRRoutingExtensions.HubPath))
                        {
                            return Task.CompletedTask;
                        }

                        var query = ctx.HttpContext.Request.Query;
                        if (
                            !query.TryGetValue("access_token", out var token)
                            || string.IsNullOrWhiteSpace(token)
                        )
                        {
                            return Task.CompletedTask;
                        }

                        ctx.Token = token.ToString();

                        // SEC-001 — rebuild the QueryString without
                        // access_token so request-logging middleware does
                        // NOT capture the bearer credential.
                        var sanitized = query
                            .Where(p =>
                                !string.Equals(
                                    p.Key,
                                    "access_token",
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                            .Select(p =>
                                $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value.ToString())}"
                            );
                        var rebuilt = string.Join("&", sanitized);
                        ctx.HttpContext.Request.QueryString =
                            rebuilt.Length == 0
                                ? QueryString.Empty
                                : new QueryString("?" + rebuilt);

                        return Task.CompletedTask;
                    },
                };
            });
        // Sprint-9 U7 — register one ASP.NET policy per
        // PermissionKeys.All entry (KTD1 + KTD4). Each policy carries
        // RequireAuthenticatedUser + RequireClaim("perm", <key>) which
        // matches the JSON-array perm claim emitted by JwtTokenIssuer.
        services.AddShopFlowPermissionPolicies();

        // ---- Sprint-9 U7 — ForwardedHeaders + RateLimiter -----------------
        // KTD7 — YARP gateway sets X-Forwarded-For; without honoring it
        // the rate-limit partition key collapses to the gateway IP and
        // every legitimate user shares one bucket. Dev defaults trust
        // loopback only; prod must explicitly allowlist gateway IPs or
        // CIDRs via Auth:ForwardedHeaders:KnownProxies /
        // Auth:ForwardedHeaders:KnownNetworks.
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownProxies.Clear();
            o.KnownNetworks.Clear();
            o.KnownNetworks.Add(
                new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("127.0.0.0"), 8)
            );
            o.KnownProxies.Add(IPAddress.IPv6Loopback);

            // Operators add gateway-side IPs via env-var binding.
            var configuredProxies = configuration
                .GetSection("Auth:ForwardedHeaders:KnownProxies")
                .Get<string[]>();
            if (configuredProxies is not null)
            {
                foreach (var p in configuredProxies)
                {
                    if (IPAddress.TryParse(p, out var ip))
                    {
                        o.KnownProxies.Add(ip);
                    }
                }
            }
            var configuredNetworks = configuration
                .GetSection("Auth:ForwardedHeaders:KnownNetworks")
                .Get<string[]>();
            if (configuredNetworks is not null)
            {
                foreach (var n in configuredNetworks)
                {
                    var parts = n.Split('/');
                    if (
                        parts.Length == 2
                        && IPAddress.TryParse(parts[0], out var net)
                        && int.TryParse(parts[1], out var prefix)
                    )
                    {
                        o.KnownNetworks.Add(
                            new Microsoft.AspNetCore.HttpOverrides.IPNetwork(net, prefix)
                        );
                    }
                }
            }
        });

        // Startup gate per KTD7 — non-Development environment with no
        // configured proxies AND no networks beyond loopback throws at
        // boot rather than running with a silently-broken partition key.
        var envName =
            configuration["ASPNETCORE_ENVIRONMENT"]
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environments.Production;
        if (!string.Equals(envName, Environments.Development, StringComparison.OrdinalIgnoreCase))
        {
            var hasProxy =
                configuration
                    .GetSection("Auth:ForwardedHeaders:KnownProxies")
                    .Get<string[]>()
                    ?.Length > 0;
            var hasNetwork =
                configuration
                    .GetSection("Auth:ForwardedHeaders:KnownNetworks")
                    .Get<string[]>()
                    ?.Length > 0;
            if (!hasProxy && !hasNetwork)
            {
                throw new InvalidOperationException(
                    "Auth:ForwardedHeaders:KnownProxies or :KnownNetworks must be set in non-Development. "
                        + "Empty allowlist silently disables forwarded-header processing AND allows direct callers "
                        + "to forge X-Forwarded-For to spoof rate-limit partition keys (Sprint-9 KTD7)."
                );
            }
        }

        // Two named rate-limit policies (KTD5):
        //   auth-credentials      → login + refresh + mfa-verify (10/min/IP)
        //   auth-forgot-password  → forgot-password (5/min/IP)
        // Both are supplementary defense per OWASP — per-account
        // lockout (users.locked_until) is the primary defense.
        services.AddRateLimiter(opts =>
        {
            opts.RejectionStatusCode = HttpStatus.Status429TooManyRequests;
            opts.OnRejected = static (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    ctx.HttpContext.Response.Headers.RetryAfter = (
                        (int)retryAfter.TotalSeconds
                    ).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                return ValueTask.CompletedTask;
            };
            opts.AddPolicy(
                "auth-credentials",
                httpCtx =>
                    RateLimitPartition.GetTokenBucketLimiter(
                        partitionKey: ClientIpKey(httpCtx),
                        factory: _ => new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = 10,
                            TokensPerPeriod = 10,
                            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true,
                        }
                    )
            );
            opts.AddPolicy(
                "auth-forgot-password",
                httpCtx =>
                    RateLimitPartition.GetTokenBucketLimiter(
                        partitionKey: ClientIpKey(httpCtx),
                        factory: _ => new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = 5,
                            TokensPerPeriod = 5,
                            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true,
                        }
                    )
            );
        });

        // ---- SignalR (Sprint-7 U5 + Sprint-7.5 U1) ------------------------
        // Single tenant-aware hub (TenantHub) mapped at /hub by Outbound.Api
        // only — see SignalRRoutingExtensions remarks. The IHubFilter binds
        // tenancy on connect + on each method invocation; registered scoped
        // because it opens its own DI scopes inside the catalog lookup.
        //
        // Sprint-7.5 U1 extends the SignalR config with .AddJsonProtocol so
        // hub event payloads (HubEventPayloads.StockChangedPayload,
        // SagaTransitionedPayload) serialize in camelCase — same wire
        // convention as the MVC controllers (AddShopFlowControllers). Without
        // this, MVC endpoints would ship camelCase while hub frames stayed
        // PascalCase — exactly the mixed-shape failure the brainstorm names
        // as the worst outcome. AddJsonProtocol owns its OWN
        // JsonSerializerOptions independent from the MVC pipeline.
        services.AddScoped<TenantBindingHubFilter>();
        services
            .AddSignalR(o =>
            {
                o.AddFilter<TenantBindingHubFilter>();
            })
            .AddJsonProtocol(opts =>
            {
                opts.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                opts.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
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

    /// <summary>
    /// Sprint-9 U7 — wires <see cref="ForwardedHeadersMiddleware"/> +
    /// <see cref="RateLimitingMiddleware"/> in the correct order
    /// (ForwardedHeaders BEFORE RateLimiter per KTD7 so the rate-limit
    /// partition key reads the real client IP, not the gateway's). Call
    /// this BEFORE <c>UseAuthentication</c>.
    /// </summary>
    public static IApplicationBuilder UseShopFlowSecurityPipeline(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseForwardedHeaders();
        app.UseRateLimiter();
        return app;
    }

    /// <summary>
    /// Rate-limit partition key extractor: prefer the
    /// <see cref="HttpContext.Connection"/> RemoteIpAddress after
    /// ForwardedHeaders has rewritten it; fall back to a constant
    /// sentinel so missing-IP requests don't share a bucket with
    /// well-formed ones.
    /// </summary>
    private static string ClientIpKey(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
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

    /// <summary>
    /// Finish-line U4 — module-supplied callback invoked inside the kernel's
    /// single <see cref="MassTransit"/> <c>AddMassTransit</c> registration,
    /// after assembly-scanned consumers/sagas and before transport selection.
    /// MassTransit forbids a second <c>AddMassTransit</c> per container, so a
    /// module that must configure a saga repository, register consumers that
    /// live outside the scanned assemblies, or add a bus-level filter sets this
    /// instead of calling <c>AddMassTransit</c> again. Outbound.Api uses it to
    /// wire the FulfillmentSaga's EntityFrameworkRepository + the SignalR relay
    /// consumers. Null (the default) leaves the bus configured by the scan +
    /// transport only, unchanged for every other module.
    /// </summary>
    public Action<IBusRegistrationConfigurator>? ConfigureBus { get; set; }
}
