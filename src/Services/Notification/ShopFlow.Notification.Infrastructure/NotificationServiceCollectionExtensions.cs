using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShopFlow.Notification.Application.Ports;
using ShopFlow.Notification.Infrastructure.BackgroundServices;
using ShopFlow.Notification.Infrastructure.Consumers;
using ShopFlow.Notification.Infrastructure.Mailers;
using ShopFlow.Notification.Infrastructure.Repositories;
using ShopFlow.Notification.Infrastructure.Templates;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Notification.Infrastructure;

/// <summary>
/// Composition root for the Notification module per AGENTS.md §11.79.
/// Wires the DbContext + repositories + mailer provider (Logging vs
/// MailKitSmtp per config) + template renderer + 4 MT consumers + the
/// background delivery dispatcher. Notification.Api's
/// <c>Program.cs</c> (U4) calls <c>services.AddShopFlowDefaults(...)</c>
/// then <c>services.AddNotificationModule(configuration)</c>.
/// </summary>
public static class NotificationServiceCollectionExtensions
{
    public const string ModuleName = "Notification";

    public static IServiceCollection AddNotificationModule(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Per-tenant DbContext bound to IRequestContext.DbConnectionString —
        // same pattern as Auth (Sprint-8 U3). PerRequestDbContextFactory<>
        // (registered by AddShopFlowDefaults) gives the dispatcher's
        // IDbContextFactory<NotificationDbContext> resolution.
        services.AddScoped<NotificationDbContext>(sp =>
        {
            var ctx = sp.GetRequiredService<IRequestContext>();
            var options = new DbContextOptionsBuilder<NotificationDbContext>()
                .UseNpgsql(
                    ctx.DbConnectionString,
                    npg =>
                        npg.MigrationsAssembly(
                            typeof(NotificationServiceCollectionExtensions).Assembly.GetName().Name
                        )
                )
                .Options;
            return new NotificationDbContext(options);
        });

        // Repositories — scoped so they bind to the per-request DbContext.
        services.AddScoped<INotificationOutboxRepository, NotificationOutboxRepository>();
        services.AddScoped<INotificationLogRepository, NotificationLogRepository>();

        // Template renderer — stateless singleton.
        services.AddSingleton<ITemplateRenderer, SimpleTemplateRenderer>();
        services.AddSingleton<TemplateResourceLoader>();

        // Mailer wiring — config-driven provider switch + transient/permanent
        // error code mapper.
        services
            .AddOptions<MailerOptions>()
            .Bind(configuration.GetSection("Notification:Mailer"));
        services.AddSingleton<SmtpResponseCodeMapper>(_ => new SmtpResponseCodeMapper());

        var providerKind = configuration.GetSection("Notification:Mailer:Provider").Value;
        if (
            string.Equals(
                providerKind,
                nameof(MailerProviderKind.MailKitSmtp),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            services.AddSingleton<IMailerProvider, MailKitSmtpMailer>();
        }
        else
        {
            services.AddSingleton<IMailerProvider, LoggingMailer>();
        }

        // Dispatcher tuning + the background service itself.
        services
            .AddOptions<NotificationDispatcherOptions>()
            .Bind(configuration.GetSection(NotificationDispatcherOptions.SectionName));
        services.AddHostedService<NotificationDeliveryDispatcher>();

        // MassTransit consumers — one per Sprint-9 cross-module Auth event.
        // AddMassTransit elsewhere (in Notification.Api Program.cs U4) will
        // call cfg.AddConsumer<T>() on each via assembly scanning OR
        // explicit registration; the consumer types themselves resolve from
        // the DI container so their scoped + singleton deps work.
        services.TryAddScoped<PasswordResetRequestedConsumer>();
        services.TryAddScoped<RefreshReuseDetectedConsumer>();
        services.TryAddScoped<AccountLockedConsumer>();
        services.TryAddScoped<MfaEnrolledConsumer>();

        return services;
    }

    /// <summary>
    /// Helper for U4's <c>AddMassTransit</c> consumer registration.
    /// Returns the 4 consumer types so the bus configuration can wire
    /// them via <c>cfg.AddConsumer&lt;T&gt;()</c> without each call site
    /// duplicating the list.
    /// </summary>
    public static IReadOnlyList<Type> GetConsumerTypes() =>
        new[]
        {
            typeof(PasswordResetRequestedConsumer),
            typeof(RefreshReuseDetectedConsumer),
            typeof(AccountLockedConsumer),
            typeof(MfaEnrolledConsumer),
        };
}
