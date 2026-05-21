using System.Net;
using System.Text;
using ShopFlow.Notification.Application.Ports;

namespace ShopFlow.Notification.Infrastructure.Templates;

/// <summary>
/// Sprint-9.5 U2 implementation of <see cref="ITemplateRenderer"/>.
/// Literal <c>{placeholder}</c> substitution only — no conditionals,
/// no loops, no <c>{{</c> escape sequences (KTD6). HTML templates
/// HTML-escape the substituted value via
/// <see cref="WebUtility.HtmlEncode(string)"/> before insertion so a
/// hostile display name carrying <c>&lt;script&gt;</c> can't smuggle
/// markup through; text templates pass values through verbatim.
/// </summary>
/// <remarks>
/// <para>Unbalanced <c>{</c> (no matching closing <c>}</c>) is treated
/// as literal text — the remainder of the template is appended
/// verbatim. A balanced <c>{key}</c> whose key is absent from the
/// variable dictionary throws <see cref="TemplateRenderException"/>;
/// this catches templates that drift from their handler's variable
/// shape during refactors.</para>
/// <para>Stateless — register as singleton.</para>
/// </remarks>
public sealed class SimpleTemplateRenderer : ITemplateRenderer
{
    public string RenderText(string templateBody, IReadOnlyDictionary<string, string> vars) =>
        Render(templateBody, vars, escape: false);

    public string RenderHtml(string templateBody, IReadOnlyDictionary<string, string> vars) =>
        Render(templateBody, vars, escape: true);

    private static string Render(
        string templateBody,
        IReadOnlyDictionary<string, string> vars,
        bool escape
    )
    {
        ArgumentNullException.ThrowIfNull(templateBody);
        ArgumentNullException.ThrowIfNull(vars);

        var sb = new StringBuilder(templateBody.Length);
        var i = 0;
        while (i < templateBody.Length)
        {
            var open = templateBody.IndexOf('{', i);
            if (open < 0)
            {
                sb.Append(templateBody, i, templateBody.Length - i);
                break;
            }

            // Append everything up to the '{'.
            sb.Append(templateBody, i, open - i);

            var close = templateBody.IndexOf('}', open + 1);
            if (close < 0)
            {
                // Unbalanced '{' — treat the remainder as literal.
                sb.Append(templateBody, open, templateBody.Length - open);
                break;
            }

            var key = templateBody.Substring(open + 1, close - open - 1);
            if (!vars.TryGetValue(key, out var value))
            {
                throw new TemplateRenderException(key);
            }

            sb.Append(escape ? WebUtility.HtmlEncode(value) : value);
            i = close + 1;
        }

        return sb.ToString();
    }
}
