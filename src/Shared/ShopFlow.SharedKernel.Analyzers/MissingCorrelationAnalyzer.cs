using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ShopFlow.SharedKernel.Analyzers;

/// <summary>
/// ShopFlow0002: flags <c>IPublishEndpoint.Publish(...)</c> calls inside
/// methods that lack visible <c>IRequestContext</c> propagation. Per
/// AGENTS.md §6.40, every published integration event must carry
/// <c>tenant_id</c>, <c>correlation_id</c>, and <c>occurred_at</c> via the
/// W3C TraceContext-compatible message envelope, sourced from
/// <c>IRequestContext</c>.
///
/// <para>
/// Heuristic: the diagnostic fires when a method invokes
/// <c>Publish</c>/<c>PublishBatch</c>/<c>Send</c> on an
/// <c>IPublishEndpoint</c> or <c>ISendEndpoint</c> and neither the method's
/// parameters nor the enclosing type's instance fields/properties expose an
/// <c>IRequestContext</c>. This is intentionally over-conservative: false
/// positives in edge cases (e.g. legitimate background tasks that build
/// their own context) are acceptable in W1 Warning mode and can be silenced
/// with a per-call <c>#pragma warning disable</c> or per-module
/// .editorconfig override.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingCorrelationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ShopFlow0002";

    private static readonly LocalizableString Title =
        "Bus publish without IRequestContext in scope";

    private static readonly LocalizableString MessageFormat =
        "'{0}' is invoked without an IRequestContext in scope — correlation/tenancy may be lost (AGENTS.md §6.40)";

    private static readonly LocalizableString Description =
        "Every published integration event must carry tenant_id, correlation_id, "
        + "and occurred_at sourced from IRequestContext. Inject IRequestContext "
        + "into the method (or the enclosing type) and pass the envelope through.";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: Title,
        messageFormat: MessageFormat,
        category: "ShopFlow.Messaging",
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
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        var name = memberAccess.Name.Identifier.ValueText;
        if (name is not ("Publish" or "PublishBatch" or "Send"))
        {
            return;
        }

        var symbol = context
            .SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken)
            .Symbol;
        if (symbol is not IMethodSymbol method)
        {
            return;
        }

        // Two shapes to consider:
        //   1. method.ContainingType is one of the MassTransit publishing
        //      interfaces (instance method on the interface itself).
        //   2. The method is an extension method (so ContainingType is a
        //      static helper class) — inspect the receiver's static type.
        if (
            !IsMassTransitPublisherType(method.ContainingType)
            && !IsExtensionOnPublisher(method, memberAccess, context)
        )
        {
            return;
        }

        var receiverTypeName = method.ContainingType?.ToDisplayString() ?? string.Empty;

        if (HasRequestContextInScope(context, invocation))
        {
            return;
        }

        // Render the diagnostic using the receiver's apparent type for
        // extension-method calls (e.g. "IPublishEndpoint.Publish") rather
        // than the static helper that owns the extension.
        var displayPrefix = method.IsExtensionMethod
            ? GetReceiverDisplayName(memberAccess, context) ?? receiverTypeName
            : receiverTypeName;
        var lastDot = displayPrefix.LastIndexOf('.');
        var displayShort = lastDot >= 0 ? displayPrefix.Substring(lastDot + 1) : displayPrefix;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invocation.GetLocation(), $"{displayShort}.{name}")
        );
    }

    private static bool IsMassTransitPublisherType(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        var displayName = type.ToDisplayString();
        if (
            displayName.StartsWith("MassTransit.IPublishEndpoint", System.StringComparison.Ordinal)
            || displayName.StartsWith("MassTransit.ISendEndpoint", System.StringComparison.Ordinal)
            || displayName.StartsWith("MassTransit.IBus", System.StringComparison.Ordinal)
        )
        {
            return true;
        }

        // Walk implemented interfaces in case the receiver type is a concrete
        // bus that inherits the interfaces.
        foreach (var iface in type.AllInterfaces)
        {
            var ifaceName = iface.ToDisplayString();
            if (
                ifaceName.StartsWith(
                    "MassTransit.IPublishEndpoint",
                    System.StringComparison.Ordinal
                )
                || ifaceName.StartsWith(
                    "MassTransit.ISendEndpoint",
                    System.StringComparison.Ordinal
                )
                || ifaceName.StartsWith("MassTransit.IBus", System.StringComparison.Ordinal)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExtensionOnPublisher(
        IMethodSymbol method,
        MemberAccessExpressionSyntax memberAccess,
        SyntaxNodeAnalysisContext context
    )
    {
        if (!method.IsExtensionMethod)
        {
            return false;
        }

        // For reduced extension methods, the receiver's type is what we want.
        var receiverType = context
            .SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken)
            .Type;
        return IsMassTransitPublisherType(receiverType);
    }

    private static string? GetReceiverDisplayName(
        MemberAccessExpressionSyntax memberAccess,
        SyntaxNodeAnalysisContext context
    )
    {
        var receiverType = context
            .SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken)
            .Type;
        return receiverType?.ToDisplayString();
    }

    private static bool HasRequestContextInScope(SyntaxNodeAnalysisContext context, SyntaxNode node)
    {
        var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method is not null)
        {
            foreach (var p in method.ParameterList.Parameters)
            {
                if (TypeIsRequestContext(context, p.Type))
                {
                    return true;
                }
            }
        }

        var typeDecl = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (typeDecl is null)
        {
            return false;
        }

        var typeSymbol = context.SemanticModel.GetDeclaredSymbol(
            typeDecl,
            context.CancellationToken
        );
        if (typeSymbol is null)
        {
            return false;
        }

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is IFieldSymbol field && IsRequestContextSymbol(field.Type))
            {
                return true;
            }

            if (member is IPropertySymbol property && IsRequestContextSymbol(property.Type))
            {
                return true;
            }
        }

        // Primary-constructor parameters surface as parameters on the type's instance constructor.
        foreach (var ctor in typeSymbol.InstanceConstructors)
        {
            foreach (var p in ctor.Parameters)
            {
                if (IsRequestContextSymbol(p.Type))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TypeIsRequestContext(SyntaxNodeAnalysisContext context, TypeSyntax? type)
    {
        if (type is null)
        {
            return false;
        }

        var info = context.SemanticModel.GetTypeInfo(type, context.CancellationToken);
        return IsRequestContextSymbol(info.Type);
    }

    private static bool IsRequestContextSymbol(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        // Match by simple name (allows test fixtures and real types alike).
        if (type.Name == "IRequestContext")
        {
            return true;
        }

        return type.AllInterfaces.Any(i => i.Name == "IRequestContext");
    }
}
