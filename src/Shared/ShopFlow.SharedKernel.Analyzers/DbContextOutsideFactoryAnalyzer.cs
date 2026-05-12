using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ShopFlow.SharedKernel.Analyzers;

/// <summary>
/// ShopFlow0003: flags <c>new TDbContext(...)</c> outside of *Factory types,
/// and string literals that look like Postgres connection strings inside
/// business code. Per ADR-0003 + AGENTS.md §3.17, every DbContext must be
/// constructed via <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/>
/// (which reads the per-request connection string from <c>IRequestContext</c>),
/// and connection strings must never be hand-built in business code — they
/// come from the control-plane catalog.
/// </summary>
/// <remarks>
/// <para>Heuristic 1 (DbContext instantiation):</para>
/// <list type="bullet">
///   <item><description>Matches <c>ObjectCreationExpressionSyntax</c> whose type is a subclass of <c>Microsoft.EntityFrameworkCore.DbContext</c>.</description></item>
///   <item><description>Exempts surrounding types whose name ends in <c>Factory</c> (e.g. <c>PerRequestDbContextFactory</c>).</description></item>
/// </list>
/// <para>Heuristic 2 (connection-string literal):</para>
/// <list type="bullet">
///   <item><description>Matches string literals that start with <c>Host=</c>, <c>Server=</c>, or contain <c>Database=</c>.</description></item>
///   <item><description>Exempts the analyzer project itself, test fixtures (any type whose name ends in <c>Fixture</c>), and any file under a path segment <c>Migrations</c>.</description></item>
/// </list>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DbContextOutsideFactoryAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ShopFlow0003";

    private static readonly LocalizableString Title =
        "DbContext / connection-string usage bypasses the per-request factory";

    private static readonly LocalizableString MessageFormat =
        "{0} — go through IDbContextFactory<T> + IRequestContext (AGENTS.md §3.17)";

    private static readonly LocalizableString Description =
        "Per ADR-0003 (DB-per-tenant) every DbContext is constructed via the "
        + "per-request factory, which reads the connection string from "
        + "IRequestContext. Hand-building a connection string or instantiating "
        + "a DbContext directly bypasses tenant routing and routes the query "
        + "to whatever DB the literal points at — a P0 cross-tenant leak risk.";

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
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeLiteral, SyntaxKind.StringLiteralExpression);
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (ObjectCreationExpressionSyntax)context.Node;

        var typeInfo = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken);
        if (typeInfo.Type is not INamedTypeSymbol created)
        {
            return;
        }

        if (!IsDbContextSubtype(created))
        {
            return;
        }

        if (IsInExemptType(context, out var _))
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                creation.GetLocation(),
                $"Direct instantiation of '{created.Name}'"
            )
        );
    }

    private static void AnalyzeLiteral(SyntaxNodeAnalysisContext context)
    {
        var literal = (LiteralExpressionSyntax)context.Node;
        var text = literal.Token.ValueText;

        if (text.Length < 8)
        {
            return;
        }

        var looksLikeConnString =
            text.StartsWith("Host=", System.StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Server=", System.StringComparison.OrdinalIgnoreCase)
            || (text.Contains("Database=") && text.Contains("="));

        if (!looksLikeConnString)
        {
            return;
        }

        if (IsInExemptType(context, out var _))
        {
            return;
        }

        // Exempt anything under a Migrations/ folder via syntax-tree path
        var path = context.Node.SyntaxTree.FilePath ?? string.Empty;
        if (path.Contains("\\Migrations\\") || path.Contains("/Migrations/"))
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                literal.GetLocation(),
                "Hard-coded connection string"
            )
        );
    }

    private static bool IsDbContextSubtype(INamedTypeSymbol type)
    {
        var cursor = type.BaseType;
        while (cursor is not null)
        {
            if (cursor.ToDisplayString() == "Microsoft.EntityFrameworkCore.DbContext")
            {
                return true;
            }
            cursor = cursor.BaseType;
        }
        return false;
    }

    private static bool IsInExemptType(SyntaxNodeAnalysisContext context, out string typeName)
    {
        typeName = string.Empty;
        var typeDecl = context.Node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (typeDecl is null)
        {
            return false;
        }

        var sym = context.SemanticModel.GetDeclaredSymbol(typeDecl, context.CancellationToken);
        if (sym is null)
        {
            return false;
        }

        typeName = sym.Name;

        return typeName.EndsWith("Factory", System.StringComparison.Ordinal)
            || typeName.EndsWith("Fixture", System.StringComparison.Ordinal)
            || typeName.EndsWith("Tests", System.StringComparison.Ordinal);
    }
}
