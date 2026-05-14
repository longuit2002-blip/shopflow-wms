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

        // Scoped DbContext bound to the active tenant (built from the
        // factory). Same pattern as Sprint-3-redux Outbound.
        services.AddScoped<ChannelDbContext>(sp =>
        {
            var factory = sp.GetRequiredService<IDbContextFactory<ChannelDbContext>>();
            return factory.CreateDbContext();
        });

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

        // ---- Signature verification (U3) ----
        services.AddSingleton<ISignatureVerifier, ShopeeSignatureVerifier>();
        services.AddSingleton<ISignatureVerifierFactory, SignatureVerifierFactory>();

        // ---- Adapter framework (U5) ----
        services.AddSingleton<ShopeeWebhookParser>();
        services.AddSingleton<IChannelAdapter, ShopeeAdapter>();
        services.AddSingleton<IChannelAdapterFactory, ChannelAdapterFactory>();

        // Polly v8 retry pipeline for outbound adapter HTTP — mirrors the
        // Sprint-3-redux MockShippingProvider pattern. The Shopee adapter
        // body lands in Sprint-5; the pipeline is registered now so the
        // Sprint-5 swap is a one-method change.
        services.AddSingleton(sp => new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Constant,
            })
            .Build());

        services.AddHttpClient<ShopeeAdapter>(client =>
        {
            var baseUrl = configuration["Channel:Shopee:MockBaseUrl"] ?? "http://localhost:5180/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

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
}
