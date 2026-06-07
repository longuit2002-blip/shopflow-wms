using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;
using Polly.Retry;
using ShopFlow.Channel.Application.Adapters;
using ShopFlow.Channel.Application.Ports;
using ShopFlow.Channel.Application.Webhooks;
using ShopFlow.Channel.Infrastructure.Adapters;
using ShopFlow.Channel.Infrastructure.Outbox;
using ShopFlow.Channel.Infrastructure.Repositories;
using ShopFlow.Channel.Infrastructure.Signature;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Channel.Infrastructure;

/// <summary>
/// Channel module composition root per Sprint-4 plan U5/U9. Mirrors the
/// Sprint-3-redux <c>AddOutboundModule</c> shape; modules call this from
/// <c>Program.cs</c> after <c>AddShopFlowDefaults</c>.
/// </summary>
public static class ChannelServiceCollectionExtensions
{
    public static IServiceCollection AddChannelModule(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ---- ChannelDbContext via IDbContextFactory (per-request scope) ----
        services.AddDbContextFactory<ChannelDbContext>(
            (sp, options) =>
            {
                var requestContext = sp.GetRequiredService<IRequestContext>();
                options.UseNpgsql(
                    requestContext.DbConnectionString,
                    npg => npg.MigrationsAssembly("ShopFlow.Channel.Infrastructure")
                );
            }
        );

        // Scoped DbContext bound to the active tenant. Build options from
        // the SCOPED sp so IRequestContext is the request-scope instance
        // that TenantRoutingMiddleware / WebhooksController.Bind populated.
        //
        // NOTE: do NOT delegate to IDbContextFactory<ChannelDbContext> here.
        // AddDbContextFactory registers a singleton factory whose options
        // builder runs against the ROOT provider, so its IRequestContext is
        // an unbound root-scope instance — which threw "tenant scope
        // accessed before the request boundary populated it" on the request
        // path the first time the receiver ran through the WAF (a never-run
        // composition gap). The factory stays registered for the background
        // MultiplexedOutboxDispatcher, which binds RequestContext in its own
        // scope before calling CreateDbContext. This scoped path mirrors
        // Sprint-3-redux Outbound's single-scoped registration.
        services.AddScoped<ChannelDbContext>(sp =>
        {
            var requestContext = sp.GetRequiredService<IRequestContext>();
            var options = new DbContextOptionsBuilder<ChannelDbContext>()
                .UseNpgsql(
                    requestContext.DbConnectionString,
                    npg => npg.MigrationsAssembly("ShopFlow.Channel.Infrastructure")
                )
                .Options;
            return new ChannelDbContext(options);
        });

        // TimeProvider for outbox row timestamps. Mirrors Outbound's
        // composition (OutboundServiceCollectionExtensions) — ChannelOutbox
        // takes TimeProvider but AddShopFlowDefaults does not register it,
        // so the outbox-append path 500'd the first time it ran through the
        // WAF (never-run composition gap surfaced by the U7 receive tests).
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);

        // ---- Application ports (U3) ----
        services.AddScoped<IWebhookEventRepository, WebhookEventRepository>();
        services.AddScoped<IChannelOutbox, ChannelOutbox>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IngestWebhookService>();
        // Sprint-4.5 U3 — orchestrator owns event-type gating + mapping +
        // OrderImportedV1 assembly. Scoped because IngestWebhookService is
        // scoped (DbContext-bound) — orchestrator inherits the scope.
        services.AddScoped<WebhookOrchestrator>();

        // ---- Product mapping (U6) ----
        services.AddScoped<IProductMappingRepository, ProductMappingRepository>();
        services.AddScoped<IProductMappingService, Mapping.HybridProductMappingService>();

        // ---- Signature verification (U3 Shopee + finish-line U7 Lazada) ----
        services.AddSingleton<ISignatureVerifier, ShopeeSignatureVerifier>();
        services.AddSingleton<ISignatureVerifier, LazadaSignatureVerifier>();
        services.AddSingleton<ISignatureVerifierFactory, SignatureVerifierFactory>();

        // ---- Adapter framework (U5 Shopee + finish-line U7 Lazada) ----
        // Finish-line U6 — extracted to AddChannelAdapterFramework so StockSync.Api
        // can register the SAME push-side adapter (ChannelAdapterFactory + the
        // real ShopeeAdapter + its typed HttpClient) without pulling in the
        // Channel module's webhook-receive schema/DbContext. The dispatcher in
        // StockSync needs IChannelAdapterFactory to push stock updates; in the
        // modular-monolith stage it composes the real adapter directly (the W6
        // split later swaps this for a cross-process call).
        services.AddChannelAdapterFramework(configuration);

        // ---- K13 Send-routing for cross-module commands (Sprint-4 U4/U8) ----
        // OrderImportedV1 is a command (point-to-point) consumed by Outbound's
        // OrderImportedConsumer — Send semantics. Default destination is
        // "order-imported-v1" (kebab-cased CLR type name); the consumer
        // registers on the matching MT endpoint.
        services.AddOutboxRoute<ShopFlow.Contracts.Channel.OrderImportedV1>(SendKind.Send);

        // ---- Outbox dispatcher (Sprint-1-redux pattern, Channel module) ----
        services.AddHostedService<MultiplexedOutboxDispatcher<ChannelDbContext>>();

        return services;
    }

    /// <summary>
    /// Registers ONLY the outbound channel-adapter framework — the
    /// <see cref="ShopeeWebhookParser"/>, the <see cref="ShopeeAdapter"/> + its
    /// typed retry-wrapped <see cref="HttpClient"/>, and the DI-enumeration
    /// <see cref="ChannelAdapterFactory"/>. Deliberately excludes the
    /// webhook-receive surface (DbContext, signature verifiers, product
    /// mapping, outbox) so a push-only consumer can compose it cheaply.
    /// </summary>
    /// <remarks>
    /// Finish-line U6 — StockSync.Api's <c>PerTenantDispatcherService</c>
    /// resolves <see cref="IChannelAdapterFactory"/> to push stock updates to
    /// the channel. Before this, StockSync.Api left the factory unregistered
    /// (the W6-split design assumed a peer composition that never landed), so
    /// the host failed DI validation at startup. In the modular-monolith stage
    /// StockSync composes the real adapter directly via this method; the W6
    /// split later swaps it for a cross-process call.
    /// </remarks>
    public static IServiceCollection AddChannelAdapterFramework(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Adding Lazada beside Shopee is a pure DI add — the factories
        // DI-enumerate IEnumerable<IChannelAdapter> / IEnumerable<ISignatureVerifier>
        // and index by ChannelType, so no factory edit is needed (plugin arch).
        services.AddSingleton<ShopeeWebhookParser>();
        services.AddSingleton<LazadaWebhookParser>();
        services.AddSingleton<IChannelAdapter, ShopeeAdapter>();
        services.AddSingleton<IChannelAdapter, LazadaAdapter>();
        services.AddSingleton<IChannelAdapterFactory, ChannelAdapterFactory>();

        // Polly v8 retry pipeline for outbound adapter HTTP — mirrors the
        // Sprint-3-redux MockShippingProvider pattern.
        services.AddSingleton(_ =>
            new ResiliencePipelineBuilder()
                .AddRetry(
                    new RetryStrategyOptions
                    {
                        MaxRetryAttempts = 3,
                        Delay = TimeSpan.FromMilliseconds(200),
                        BackoffType = DelayBackoffType.Constant,
                    }
                )
                .Build()
        );

        services.AddHttpClient<ShopeeAdapter>(client =>
        {
            var baseUrl = configuration["Channel:Shopee:MockBaseUrl"] ?? "http://localhost:5180/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient<LazadaAdapter>(client =>
        {
            var baseUrl = configuration["Channel:Lazada:MockBaseUrl"] ?? "http://localhost:5182/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
