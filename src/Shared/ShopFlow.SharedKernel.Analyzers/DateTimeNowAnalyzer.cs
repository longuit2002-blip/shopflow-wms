using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ShopFlow.SharedKernel.Analyzers;

/// <summary>
/// ShopFlow0004: forbids <c>DateTime.Now</c>, <c>DateTime.Today</c>, and
/// <c>DateTimeOffset.Now</c>. Per AGENTS.md §5.31/§5.32, prefer
/// <c>DateTime.UtcNow</c>, <c>DateTimeOffset.UtcNow</c>, or — better still —
/// inject <c>TimeProvider</c>. <c>.Now</c> couples logic to the host's local
/// timezone, which produces incident-flavoured surprises in distributed
/// systems and during DST boundaries.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DateTimeNowAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ShopFlow0004";

    private static readonly LocalizableString Title =
        "Local-clock DateTime/DateTimeOffset access is forbidden";

    private static readonly LocalizableString MessageFormat =
        "'{0}' is forbidden — use UtcNow or inject TimeProvider (AGENTS.md §5.31)";

    private static readonly LocalizableString Description =
        "DateTime.Now, DateTime.Today, and DateTimeOffset.Now read the host's local "
        + "timezone. ShopFlow normalises on UTC end-to-end; for testability inject "
        + "TimeProvider rather than calling UtcNow directly.";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: Title,
        messageFormat: MessageFormat,
        category: "ShopFlow.Time",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/longuit2002-blip/shopflow-wms/blob/main/AGENTS.md#5-async-time-and-concurrency"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            AnalyzeMemberAccess,
            SyntaxKind.SimpleMemberAccessExpression
        );
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        var memberName = memberAccess.Name.Identifier.ValueText;

        if (memberName != "Now" && memberName != "Today")
        {
            return;
        }

        var symbol = context
            .SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken)
            .Symbol;
        if (symbol is not IPropertySymbol propertySymbol)
        {
            return;
        }

        var containingType = propertySymbol.ContainingType?.ToDisplayString();
        if (containingType is not ("System.DateTime" or "System.DateTimeOffset"))
        {
            return;
        }

        // DateTimeOffset has no Today; DateTime has both Now and Today. Either way,
        // both .Now properties and DateTime.Today are violations.
        if (containingType == "System.DateTimeOffset" && memberName == "Today")
        {
            return;
        }

        var display = $"{containingType.Substring("System.".Length)}.{memberName}";
        context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.GetLocation(), display));
    }
}
