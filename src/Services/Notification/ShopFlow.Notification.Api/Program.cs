// ─────────────────────────────────────────────────────────────────────────
// Sprint-9.5 U1 stub. The Notification module quartet ships scaffolded
// but inert: only /health responds, and there is no AddNotificationModule
// composition yet. U3 wires the four MT consumers + repositories + mailer
// switch; U4 lands the Aspire Mailpit reference + the
// MultiplexedOutboxDispatcher<NotificationDbContext> background service +
// the production appsettings.json. Until then this host process is a
// no-op apart from health probes.
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", module = "Notification" }));

await app.RunAsync().ConfigureAwait(false);

/// <summary>Marker for the typed logger in <c>Program</c> (top-level statements have no class).</summary>
public partial class Program;
