using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ShopFlow.SharedKernel.Analyzers;

/// <summary>
/// ShopFlow0003: flags ASP.NET Core action methods that look like webhook
/// receivers but lack the <c>[Idempotent]</c> attribute. Per AGENTS.md
/// §6.36/§6.37, webhook receivers must persist
/// <c>(channel_id, provider_event_id) UNIQUE</c> before enqueuing for
/// processing; the <c>[Idempotent]</c> marker is the canonical signal that
/// this discipline is in place (and the place a future filter/middleware
/// hooks into for cross-cutting wiring).
///
/// <para>
/// Heuristic: a method is a webhook handler if it carries an HTTP-verb
/// attribute (<c>[HttpPost]</c>, <c>[HttpPut]</c>, <c>[HttpPatch]</c>) AND
/// either (a) the method name starts with <c>Webhook</c> or <c>Receive</c>,
/// or (b) one of its routing attributes mentions <c>/webhook</c> in the
/// template. Methods routed via <c>[Route]</c> on the controller class are
/// not currently inspected; that's a known limitation for W1 Warning mode.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingIdempotentAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ShopFlow0003";

    private static readonly LocalizableString Title =
        "Webhook handler missing [Idempotent] attribute";

    private static readonly LocalizableString MessageFormat =
        "Webhook handler '{0}' is missing the [Idempotent] attribute (AGENTS.md §6.37)";

    private static readonly LocalizableString Description =
        "Webhook receivers must persist the raw payload + "
        + "(channel_id, provider_event_id) UNIQUE before enqueuing for processing. "
        + "Mark the action [Idempotent] to declare this discipline; the attribute "
        + "is the hook for cross-cutting filters and the diagnostic for this rule.";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: Title,
        messageFormat: MessageFormat,
        category: "ShopFlow.Idempotency",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/longuit2002-blip/shopflow-wms/blob/main/AGENTS.md#6-outbox-messaging-and-idempotency"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;

        if (!HasHttpVerbAttribute(method, out var routeTemplate))
        {
            return;
        }

        if (!LooksLikeWebhook(method, routeTemplate))
        {
            return;
        }

        if (HasIdempotentAttribute(method))
        {
            return;
        }

        var name = method.Identifier.ValueText;
        context.ReportDiagnostic(Diagnostic.Create(Rule, method.Identifier.GetLocation(), name));
    }

    private static bool HasHttpVerbAttribute(
        MethodDeclarationSyntax method,
        out string? routeTemplate
    )
    {
        routeTemplate = null;
        foreach (var list in method.AttributeLists)
        {
            foreach (var attr in list.Attributes)
            {
                var name = AttributeName(attr);
                if (name is "HttpPost" or "HttpPut" or "HttpPatch" or "HttpDelete")
                {
                    routeTemplate ??= ExtractFirstStringArgument(attr);
                    return true;
                }

                if (name == "Route")
                {
                    routeTemplate ??= ExtractFirstStringArgument(attr);
                }
            }
        }
        return false;
    }

    private static bool LooksLikeWebhook(MethodDeclarationSyntax method, string? routeTemplate)
    {
        var name = method.Identifier.ValueText;
        if (name.StartsWith("Webhook", System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.StartsWith("Receive", System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (
            routeTemplate is not null
            && routeTemplate.IndexOf("webhook", System.StringComparison.OrdinalIgnoreCase) >= 0
        )
        {
            return true;
        }

        return false;
    }

    private static bool HasIdempotentAttribute(MethodDeclarationSyntax method)
    {
        return method
            .AttributeLists.SelectMany(l => l.Attributes)
            .Any(a => AttributeName(a) == "Idempotent");
    }

    private static string AttributeName(AttributeSyntax attribute)
    {
        var raw = attribute.Name.ToString();
        var lastDot = raw.LastIndexOf('.');
        var simple = lastDot >= 0 ? raw.Substring(lastDot + 1) : raw;
        return simple.EndsWith("Attribute", System.StringComparison.Ordinal)
            ? simple.Substring(0, simple.Length - "Attribute".Length)
            : simple;
    }

    private static string? ExtractFirstStringArgument(AttributeSyntax attribute)
    {
        var arg = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
        if (arg is LiteralExpressionSyntax literal && literal.Token.Value is string s)
        {
            return s;
        }
        return null;
    }
}
