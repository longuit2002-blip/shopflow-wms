using MassTransit;
using ShopFlow.ControlPlane.Infrastructure;
using ShopFlow.Notification.Infrastructure;
using ShopFlow.SharedKernel.Infrastructure;

// ─────────────────────────────────────────────────────────────────────────
// ShopFlow.Notification.Api — hosted-service host (Sprint-9.5 U4).
// Composition per AGENTS.md §11.79:
//   1. services.AddShopFlowDefaults(configuration) — kernel cross-cutting
//      (MassTransit + RabbitMQ wiring incl. consumer scanning,
//      IRequestContext, OutboxInterceptor, PerRequestDbContextFactory,
//      OpenTelemetry).
//   2. services.AddControlPlane(configuration) — ITenantCatalog so the
//      NotificationDeliveryDispatcher can iterate every Ready tenant.
//   3. services.AddNotificationModule(configuration) — NotificationDbContext,
//      INotificationOutboxRepository + INotificationLogRepository, the
//      ITemplateRenderer + IMailerProvider (LoggingMailer in dev unless
//      overridden), and the NotificationDeliveryDispatcher background
//      service.
//
// No public REST surface beyond /health — emails arrive via consumed
// Sprint-9 cross-module events (PasswordResetRequestedV1,
// RefreshReuseDetectedV1, AccountLockedV1, MfaEnrolledV1). The 4 MT
// consumers are registered through AddShopFlowDefaults' MassTransit
// scanning over the Notification.Infrastructure assembly.
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddShopFlowDefaults(
    builder.Configuration,
    configure: o => o.ServiceName = "shopflow-notification",
    assembliesToScan: new[] { typeof(NotificationDbContext).Assembly }
);
builder.Services.AddControlPlane(builder.Configuration);
builder.Services.AddNotificationModule(builder.Configuration);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", module = "Notification" }));

await app.RunAsync().ConfigureAwait(false);

/// <summary>Marker for the typed logger in <c>Program</c> (top-level statements have no class).</summary>
public partial class Program;
