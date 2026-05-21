namespace ShopFlow.Notification.Infrastructure.Mailers;

/// <summary>
/// Notification mailer configuration bound from <c>Notification:Mailer:*</c>
/// in appsettings. <see cref="Provider"/> selects the live mailer at
/// composition time; <see cref="MailKitSmtp"/> carries the SMTP wire
/// settings (Mailpit in dev via Aspire DNS host alias <c>mailpit</c>,
/// real provider — Sendgrid / SES / Resend / etc. — in prod).
/// </summary>
public sealed class MailerOptions
{
    public MailerProviderKind Provider { get; set; } = MailerProviderKind.Logging;

    public MailKitSmtpOptions MailKitSmtp { get; set; } = new();
}

/// <summary>
/// Which <c>IMailerProvider</c> implementation is wired at startup.
/// </summary>
public enum MailerProviderKind
{
    /// <summary>Dev-safety default — writes a structured log line, never sends.</summary>
    Logging = 0,

    /// <summary>MailKit-backed SMTP. Aspire-managed Mailpit in dev; real SMTP in prod.</summary>
    MailKitSmtp = 1,
}

/// <summary>
/// SMTP wire settings consumed by <c>MailKitSmtpMailer</c>. Bound from
/// <c>Notification:Mailer:MailKitSmtp:*</c>. The Aspire AppHost wires
/// <see cref="Host"/> = <c>mailpit</c> + <see cref="Port"/> = <c>1025</c>
/// in dev; production override comes from environment variables /
/// secrets per the operational pre-flight checklist.
/// </summary>
public sealed class MailKitSmtpOptions
{
    /// <summary>SMTP server hostname.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>SMTP server port. 25 = plain, 587 = submission with STARTTLS, 465 = SMTPS.</summary>
    public int Port { get; set; } = 25;

    /// <summary>SASL PLAIN username; empty/null = anonymous auth (Mailpit dev).</summary>
    public string? Username { get; set; }

    /// <summary>SASL PLAIN password; empty/null = anonymous auth.</summary>
    public string? Password { get; set; }

    /// <summary>From-address shown on every outbound email.</summary>
    public string FromEmail { get; set; } = "noreply@shopflow.local";

    /// <summary>Display name shown on every outbound email.</summary>
    public string FromDisplayName { get; set; } = "ShopFlow WMS";

    /// <summary>Opportunistic STARTTLS upgrade. False for Mailpit dev; true for any real provider.</summary>
    public bool UseStartTls { get; set; }
}
