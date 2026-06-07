namespace ShopFlow.Notification.Application.Ports;

/// <summary>
/// Boundary the U3 MT consumers use to materialise the rendered subject
/// and body strings for a given <c>NotificationKind</c> + flat scalar
/// dictionary. The Sprint-9.5 U2 implementation (<c>SimpleTemplateRenderer</c>)
/// is literal <c>{placeholder}</c> substitution per KTD6 — no
/// conditionals, no loops, no escape sequences. HTML templates HTML-
/// escape values before substitution; text templates pass them through
/// verbatim.
/// </summary>
/// <remarks>
/// <para>Templates ship as embedded resources under
/// <c>ShopFlow.Notification.Infrastructure/Templates/</c> following the
/// naming convention <c>&lt;kind&gt;.{txt|html}.tmpl</c> (e.g.
/// <c>password-reset.txt.tmpl</c>). U3 lands the 8 resources + the
/// embedded-resource lookup logic.</para>
/// </remarks>
public interface ITemplateRenderer
{
    /// <summary>
    /// Render the plain-text body of a notification kind with the given
    /// scalar variables. Raw passthrough — no HTML escaping.
    /// </summary>
    /// <exception cref="TemplateRenderException">A <c>{placeholder}</c>
    /// in the template has no matching key in <paramref name="vars"/>.</exception>
    string RenderText(string templateBody, IReadOnlyDictionary<string, string> vars);

    /// <summary>
    /// Render the HTML body of a notification kind. Values supplied via
    /// <paramref name="vars"/> are HTML-escaped before substitution so a
    /// hostile display name carrying <c>&lt;script&gt;</c> can't smuggle
    /// markup through.
    /// </summary>
    /// <exception cref="TemplateRenderException">A <c>{placeholder}</c>
    /// in the template has no matching key in <paramref name="vars"/>.</exception>
    string RenderHtml(string templateBody, IReadOnlyDictionary<string, string> vars);
}

/// <summary>
/// Thrown when a template references a placeholder name absent from the
/// caller's variable dictionary. Halts the U3 consumer and surfaces as a
/// stable error (the MT consume is retried by the broker; if the
/// template + payload mismatch is deterministic the redelivery loop will
/// eventually move it to the broker's DLQ).
/// </summary>
public sealed class TemplateRenderException : Exception
{
    public string MissingKey { get; }

    public TemplateRenderException(string missingKey)
        : base(
            $"template placeholder '{{{missingKey}}}' has no matching value in the render dictionary."
        )
    {
        MissingKey = missingKey;
    }
}
