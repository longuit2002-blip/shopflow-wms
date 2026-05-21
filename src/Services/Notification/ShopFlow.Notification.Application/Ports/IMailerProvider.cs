using ShopFlow.Notification.Domain.ValueObjects;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Notification.Application.Ports;

/// <summary>
/// Newtype wrapping the SMTP server's <c>Message-Id</c> response value
/// (e.g. <c>&lt;a1b2c3@example.com&gt;</c>). Captured into
/// <c>notification_log.message_id</c> for delivery tracing.
/// </summary>
public readonly record struct MessageId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// Outbound transactional-email boundary. The U3 background dispatcher
/// holds an <see cref="IMailerProvider"/> singleton and calls
/// <see cref="SendAsync"/> once per claimed <c>notification_outbox</c>
/// row. The provider is selected at composition time by
/// <c>MailerOptions.Provider</c> (Logging vs MailKitSmtp).
/// </summary>
/// <remarks>
/// <para>Per KTD4 the result carries stable error codes — <c>mailer.transient.*</c>
/// is retryable (the dispatcher will bump <c>attempt_count</c> + keep
/// the outbox row); <c>mailer.permanent.*</c> is terminal (the
/// dispatcher writes a <c>notification_dead_letter</c> row + deletes
/// the outbox row immediately, no retries).</para>
/// </remarks>
public interface IMailerProvider
{
    Task<Result<MessageId>> SendAsync(RenderedEmail email, Recipient recipient, CancellationToken ct);
}
