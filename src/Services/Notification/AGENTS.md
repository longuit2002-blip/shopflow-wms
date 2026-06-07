# Notification module — agent invariants

Sprint-9.5 ships the 7th business module — consumes Auth's 4 cross-module events and delivers transactional email via SMTP. Modular monolith stage: hosted-service host shape (no public REST surface beyond `/health`).

## Hard rules

- **KTD1 quartet** — Domain (value objects + entities only) + Application (pure ports) + Infrastructure (DbContext + EF + consumers + mailers + templates) + Api (hosted-service host). Domain references SharedKernel only.
- **KTD2 two-stage delivery** — MT consumer renders the template + writes to `notification_outbox` (status=pending) + acks the MT message. The background dispatcher polls `notification_outbox` + calls `IMailerProvider.SendAsync`. Decouples MT consumer throughput from SMTP latency. Consumers never call the mailer directly.
- **KTD3 idempotency** — `UNIQUE(source_event_id, recipient_email)` on `notification_log` is the dedup anchor. Duplicate MT redelivery races an outbox row; the dispatcher's `INSERT notification_log` fails on UNIQUE; the duplicate outbox row is silently dropped at debug log level. No double-send.
- **KTD4 stable error codes** — `IMailerProvider.SendAsync` returns `Result<MessageId>` with `mailer.transient.*` (retryable) vs `mailer.permanent.*` (terminal). 4xx → transient; 5xx → permanent. Per-provider overrides via `SmtpResponseCodeMapper`.
- **KTD5 plaintext stays in Auth** — `PasswordResetRequestedV1.ResetLinkUrl` arrives pre-built. Notification never composes URL templates or sees plaintext tokens.
- **KTD6 template renderer is literal `{placeholder}`** — no conditionals, no loops, no `{{` escape sequences. HTML templates HTML-escape recipient-supplied content (display name etc.) before substitution.
- **KTD7 Mailpit tag pinned** — `axllent/mailpit:v1.21.0`. No `:latest`.
- **ADR-0003** — per-tenant DB. No `tenant_id` columns on Notification tables; the database identity IS the tenant boundary. `Recipient.TenantId` is in-process metadata only.

## Pointers

- Domain: `ShopFlow.Notification.Domain` (3 value objects: `NotificationKind` enum + `Recipient` + `RenderedEmail`; 3 entities: `NotificationOutboxEntry` + `NotificationLogEntry` + `NotificationDeadLetterEntry`).
- Application ports: `ShopFlow.Notification.Application.Ports` (`INotificationOutboxRepository`, `INotificationLogRepository`, `IMailerProvider` (U2), `ITemplateRenderer` (U2)).
- Infrastructure: `ShopFlow.Notification.Infrastructure` (EF context, initial migration, EF configurations, mailers (U2), templates (U2), 4 consumers (U3), composition extension `AddNotificationModule` (U3)).
- 3 tables (per-tenant DB): `notification_outbox` (rendered emails awaiting dispatch), `notification_log` (terminal success + idempotency UNIQUE), `notification_dead_letter` (terminal failure).
- 4 consumers (U3): `PasswordResetRequestedConsumer` + `RefreshReuseDetectedConsumer` + `AccountLockedConsumer` + `MfaEnrolledConsumer`. Each binds 1 Sprint-9 cross-module event.
- 4 templates × 2 mime types = 8 embedded resources under `Templates/`. English-only at Sprint-9.5; bilingual waits for `users.preferred_locale` (Sprint-10+).
- Tests: `tests/ShopFlow.Notification.UnitTests/` (smoke + value-object + template + mailer + consumer harness tests via MT TestHarness + NSubstitute).
