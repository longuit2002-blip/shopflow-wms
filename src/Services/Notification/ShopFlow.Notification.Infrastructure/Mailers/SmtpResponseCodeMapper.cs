using MailKit.Net.Smtp;

namespace ShopFlow.Notification.Infrastructure.Mailers;

/// <summary>
/// Maps MailKit <see cref="SmtpCommandException"/> status codes to the
/// stable <c>mailer.transient.*</c> / <c>mailer.permanent.*</c> error
/// codes per KTD4. The U3 dispatcher uses the code prefix to decide
/// whether to bump <c>attempt_count</c> + retry (transient) or move
/// straight to <c>notification_dead_letter</c> (permanent).
/// </summary>
/// <remarks>
/// <para>RFC 5321 §4.2 defines the standard taxonomy — 4xx (Transient
/// Negative Completion) means "try again later", 5xx (Permanent
/// Negative Completion) means "don't retry". The default mapping
/// follows that boundary verbatim.</para>
/// <para>Some providers misuse the codes (e.g. a 5xx for a transient
/// quota / rate-limit case). The <c>overrides</c> dictionary lets
/// composition wire per-status-code overrides
/// (e.g. <c>{ 552: "mailer.transient.quota_exceeded" }</c> for
/// Sendgrid-style quotas).</para>
/// </remarks>
public sealed class SmtpResponseCodeMapper
{
    private readonly IReadOnlyDictionary<int, string> _overrides;

    public SmtpResponseCodeMapper(IReadOnlyDictionary<int, string>? overrides = null)
    {
        _overrides = overrides ?? new Dictionary<int, string>();
    }

    /// <summary>
    /// Translate a MailKit <see cref="SmtpCommandException"/> to a
    /// <c>(errorCode, message)</c> tuple. Per-instance overrides win;
    /// otherwise the RFC 5321 4xx/5xx split applies.
    /// </summary>
    public (string ErrorCode, string Message) Map(SmtpCommandException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        var statusCode = (int)ex.StatusCode;

        if (_overrides.TryGetValue(statusCode, out var overrideCode))
        {
            return (overrideCode, ex.Message);
        }

        if (statusCode is >= 400 and <= 499)
        {
            return ("mailer.transient.smtp_4xx", ex.Message);
        }

        if (statusCode is >= 500 and <= 599)
        {
            return ("mailer.permanent.smtp_5xx", ex.Message);
        }

        // Status outside 4xx/5xx — defensive default. RFC 5321 doesn't
        // foresee this for a CommandException leg, but if a server
        // smuggles a 2xx/3xx into the failure path treat as permanent
        // so it doesn't loop forever.
        return ("mailer.permanent.unknown", ex.Message);
    }
}
