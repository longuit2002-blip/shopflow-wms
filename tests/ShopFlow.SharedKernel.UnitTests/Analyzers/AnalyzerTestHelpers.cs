using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace ShopFlow.SharedKernel.UnitTests.Analyzers;

/// <summary>
/// Pre-configured CSharpAnalyzerTest harness for ShopFlow analyzers.
///   • LanguageVersion → C# latest, so file-scoped namespaces and primary
///     constructors parse without test-source noise.
///   • ReferenceAssemblies → .NET 8.0, the project's runtime target.
///   • TestState.AdditionalReferences seeded with the runtime kernel and a
///     handful of marketplace stub references (MassTransit, EF Core, ASP.NET
///     attributes) so test sources can refer to MassTransit / EF Core /
///     ASP.NET types without importing them by full assembly path.
///
/// The base class deliberately stays minimal — each analyzer's test file
/// constructs its own snippet and asserts via fluent <c>VerifyAnalyzerAsync</c>.
/// </summary>
internal sealed class ShopFlowAnalyzerTest<TAnalyzer>
    : CSharpAnalyzerTest<TAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    public ShopFlowAnalyzerTest()
    {
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80;

        // Bring in the runtime kernel and analyzer assemblies so test
        // sources can reference IRequestContext, IdempotentAttribute, etc.
        AddReference(typeof(global::ShopFlow.SharedKernel.Application.IRequestContext));
        AddReference(
            typeof(global::ShopFlow.SharedKernel.Application.Attributes.IdempotentAttribute)
        );

        // Marketplace assemblies whose types the analyzers match against.
        AddReference(typeof(MassTransit.IPublishEndpoint));
        AddReference(typeof(Microsoft.EntityFrameworkCore.DbContext));
        AddReference(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute));
        AddReference(typeof(Microsoft.AspNetCore.Mvc.ControllerBase));
        // IActionResult lives in Microsoft.AspNetCore.Mvc.Abstractions, a
        // separate assembly from HttpPostAttribute. Test snippets that return
        // IActionResult won't compile without this reference.
        AddReference(typeof(Microsoft.AspNetCore.Mvc.IActionResult));
    }

    private void AddReference(Type type)
    {
        TestState.AdditionalReferences.Add(
            MetadataReference.CreateFromFile(type.Assembly.Location)
        );
    }

    protected override ParseOptions CreateParseOptions() =>
        ((CSharpParseOptions)base.CreateParseOptions()).WithLanguageVersion(LanguageVersion.Latest);

    protected override CompilationOptions CreateCompilationOptions()
    {
        var options = (CSharpCompilationOptions)base.CreateCompilationOptions();
        // Suppress noise diagnostics that are unrelated to the rules we're
        // testing — without this, missing-XML-doc / nullable warnings turn
        // every test source into a wall of red.
        var suppressed = new[]
        {
            "CS1591", // Missing XML comment
            "CS8019", // Unnecessary using directive
        };
        var modified = options.SpecificDiagnosticOptions;
        foreach (var id in suppressed)
        {
            modified = modified.SetItem(id, ReportDiagnostic.Suppress);
        }
        return options.WithSpecificDiagnosticOptions(modified);
    }
}
