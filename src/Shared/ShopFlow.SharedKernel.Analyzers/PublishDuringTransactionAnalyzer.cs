using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ShopFlow.SharedKernel.Analyzers;

/// <summary>
/// ShopFlow0002: flags direct <c>IPublishEndpoint.Publish</c> / <c>PublishAsync</c>
/// calls from non-consumer / non-saga code. Per AGENTS.md §6.38 modules must
/// publish through the outbox pattern (raise domain event → interceptor
/// writes outbox row → multiplexed dispatcher publishes) so the publish is
/// atomic with the business write. Direct calls during a write transaction
/// risk the classic "DB row committed but message lost" failure.
/// </summary>
/// <remarks>
/// Heuristic: matches invocations whose receiver type is
/// <c>MassTransit.IPublishEndpoint</c> (or a derived interface like
/// <c>IBus</c>) and reports a diagnostic unless the surrounding type's name
/// ends in <c>Consumer</c>, <c>Saga</c>, or <c>OutboxDispatcher</c>. Those
/// are the legitimate publish sites — consumers re-publish derived events;
/// sagas emit state-transition events; the dispatcher is the outbox sink.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PublishDuringTransactionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ShopFlow0002";

    private static readonly LocalizableString Title =
        "Direct IPublishEndpoint usage bypasses the outbox pattern";

    private static readonly LocalizableString MessageFormat =
        "Publishing '{0}' from '{1}' bypasses the outbox — raise a domain event and let the dispatcher publish (AGENTS.md §6.38)";

    private static readonly LocalizableString Description =
        "Calling IPublishEndpoint.Publish during a write transaction risks the "
        + "atomicity hole the outbox pattern was introduced to close (DB row "
        + "committed, message lost). Aggregate roots should raise domain events; "
        + "the OutboxInterceptor writes outbox rows in the same transaction, "
        + "and the multiplexed OutboxDispatcher publishes them to the bus. "
        + "Consumer / Saga / OutboxDispatcher types are the legitimate exceptions.";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: Title,
        messageFormat: MessageFormat,
        category: "ShopFlow.Messaging",
        defaultSeverity: DiagnosticSeverity.Error,
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

        var methodName = memberAccess.Name.Identifier.ValueText;
        if (methodName is not ("Publish" or "PublishAsync"))
        {
            return;
        }

        var receiverInfo = context.SemanticModel.GetTypeInfo(
            memberAccess.Expression,
            context.CancellationToken
        );
        var receiverType = receiverInfo.Type;
        if (receiverType is null)
        {
            return;
        }

        if (!ImplementsMassTransitPublishEndpoint(receiverType))
        {
            return;
        }

        if (!IsInForbiddenLayer(context, out var enclosingTypeName))
        {
            return;
        }

        var firstArgument = invocation.ArgumentList.Arguments.FirstOrDefault()?.ToString() ?? "?";
        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invocation.GetLocation(), firstArgument, enclosingTypeName)
        );
    }

    private static bool ImplementsMassTransitPublishEndpoint(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named)
        {
            if (named.ToDisplayString() == "MassTransit.IPublishEndpoint")
            {
                return true;
            }

            foreach (var i in named.AllInterfaces)
            {
                if (i.ToDisplayString() == "MassTransit.IPublishEndpoint")
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool IsInForbiddenLayer(
        SyntaxNodeAnalysisContext context,
        out string enclosingTypeName
    )
    {
        enclosingTypeName = string.Empty;

        var typeDecl = context.Node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
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

        enclosingTypeName = typeSymbol.Name;

        // Legitimate publish sites: consumers (handle bus messages), sagas
        // (state-machine emits), the multiplexed outbox dispatcher itself.
        return !(
            enclosingTypeName.EndsWith("Consumer", System.StringComparison.Ordinal)
            || enclosingTypeName.EndsWith("Saga", System.StringComparison.Ordinal)
            || enclosingTypeName.Contains("OutboxDispatcher")
        );
    }
}
