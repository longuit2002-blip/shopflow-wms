using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ShopFlow.SharedKernel.Analyzers;

/// <summary>
/// ShopFlow0001: flags raw <c>DbSet&lt;T&gt;</c> access from Application or Api
/// layers. Tenant scoping is enforced by repositories; bypassing them is the
/// canonical multi-tenancy bug (a missing <c>WHERE tenant_id = …</c> still
/// returns rows from every other tenant if RLS is not in force).
///
/// <para>
/// Heuristic: matches <c>dbContext.Set&lt;T&gt;()</c> and direct DbSet property
/// access (any property typed <c>DbSet&lt;T&gt;</c>) when the surrounding type
/// lives in a namespace whose tail segment is <c>Application</c> or <c>Api</c>.
/// Code in <c>Infrastructure.Repositories</c> is exempt because that's where
/// the tenant-scoped wrappers live.
/// </para>
///
/// <para>
/// Severity is Warning by default — analyzers can't perfectly know layer
/// boundaries (esp. across project files), so false positives are tolerated
/// in W1. Promotion to Error gates on a 7-day clean window per AGENTS.md
/// §9.65 and the U11 promotion plan.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawDbSetAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ShopFlow0001";

    private static readonly LocalizableString Title =
        "Raw DbSet access bypasses tenant-scoped repository";

    private static readonly LocalizableString MessageFormat =
        "Raw DbSet<{0}> access in '{1}' bypasses tenant scoping — go through a repository (AGENTS.md §3.16)";

    private static readonly LocalizableString Description =
        "Application and Api layers must not query DbSets directly. Tenant-scoped "
        + "repositories own the WHERE tenant_id = … guarantee even if RLS is "
        + "misconfigured. Move this query behind an I*Repository interface in "
        + "Application/Ports, with the implementation in Infrastructure/Repositories.";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: Title,
        messageFormat: MessageFormat,
        category: "ShopFlow.Tenancy",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/longuit2002-blip/shopflow-wms/blob/main/AGENTS.md#3-multi-tenancy-and-data-access"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(
            AnalyzeMemberAccess,
            SyntaxKind.SimpleMemberAccessExpression
        );
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        if (memberAccess.Name is not GenericNameSyntax { Identifier.ValueText: "Set" } generic)
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

        if (method.ContainingType?.ToDisplayString() != "Microsoft.EntityFrameworkCore.DbContext")
        {
            return;
        }

        if (!IsInRestrictedLayer(context, out var layer))
        {
            return;
        }

        var typeArgument = generic.TypeArgumentList.Arguments.FirstOrDefault()?.ToString() ?? "?";
        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invocation.GetLocation(), typeArgument, layer)
        );
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;

        // Skip the LHS of an invocation that AnalyzeInvocation already handles.
        if (memberAccess.Parent is InvocationExpressionSyntax)
        {
            return;
        }

        var symbol = context
            .SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken)
            .Symbol;
        if (symbol is not IPropertySymbol property)
        {
            return;
        }

        if (!IsDbSetType(property.Type))
        {
            return;
        }

        if (!IsInRestrictedLayer(context, out var layer))
        {
            return;
        }

        var typeArgument =
            property.Type is INamedTypeSymbol named && named.TypeArguments.Length == 1
                ? named.TypeArguments[0].Name
                : "?";
        context.ReportDiagnostic(
            Diagnostic.Create(Rule, memberAccess.GetLocation(), typeArgument, layer)
        );
    }

    private static bool IsDbSetType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        var openName = named.ConstructedFrom?.ToDisplayString();
        return openName == "Microsoft.EntityFrameworkCore.DbSet<TEntity>";
    }

    /// <summary>
    /// Layer detection by namespace tail. Returns true and sets <paramref name="layer"/>
    /// to "Application" or "Api" when the surrounding type's namespace ends in one of
    /// those segments. Skips types in *Infrastructure.Repositories* because that's
    /// where the tenant-scoped repository implementations legitimately query DbSets.
    /// </summary>
    private static bool IsInRestrictedLayer(SyntaxNodeAnalysisContext context, out string layer)
    {
        layer = string.Empty;

        var typeDecl = context.Node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (typeDecl is null)
        {
            return false;
        }

        var typeSymbol = context.SemanticModel.GetDeclaredSymbol(
            typeDecl,
            context.CancellationToken
        );
        var namespaceName = typeSymbol?.ContainingNamespace?.ToDisplayString();
        if (string.IsNullOrEmpty(namespaceName))
        {
            return false;
        }

        // Carve out repository implementations.
        if (namespaceName!.Contains(".Infrastructure.Repositories"))
        {
            return false;
        }

        var segments = namespaceName.Split('.');
        for (int i = segments.Length - 1; i >= 0; i--)
        {
            if (segments[i] == "Application")
            {
                layer = "Application";
                return true;
            }

            if (segments[i] == "Api")
            {
                layer = "Api";
                return true;
            }

            // Don't walk past Infrastructure — it's a legitimate layer for
            // DbSet access from inside Repositories/.
            if (segments[i] == "Infrastructure")
            {
                return false;
            }
        }

        return false;
    }
}
