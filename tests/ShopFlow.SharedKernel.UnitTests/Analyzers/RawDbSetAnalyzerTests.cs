using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using ShopFlow.SharedKernel.Analyzers;
using Xunit;

namespace ShopFlow.SharedKernel.UnitTests.Analyzers;

public class RawDbSetAnalyzerTests
{
    [Fact]
    public async Task SetGeneric_FromApplicationLayer_Flagged()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;

            namespace MyModule.Application
            {
                public class MyHandler
                {
                    private readonly DbContext _db;
                    public MyHandler(DbContext db) { _db = db; }
                    public object Get() => {|#0:_db.Set<Widget>()|};
                }

                public class Widget { public int Id { get; set; } }
            }
            """;

        await new ShopFlowAnalyzerTest<RawDbSetAnalyzer>
        {
            TestCode = source,
            ExpectedDiagnostics =
            {
                new DiagnosticResult(RawDbSetAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                    .WithLocation(0)
                    .WithArguments("Widget", "Application"),
            },
        }.RunAsync();
    }

    [Fact]
    public async Task DbSetProperty_FromApiLayer_Flagged()
    {
        // The DbContext type lives in Infrastructure (legitimate); the
        // forbidden access happens from Api.
        const string source = """
            using Microsoft.EntityFrameworkCore;

            namespace MyModule.Infrastructure
            {
                public class MyDb : DbContext
                {
                    public DbSet<Widget> Widgets => Set<Widget>();
                }

                public class Widget { public int Id { get; set; } }
            }

            namespace MyModule.Api
            {
                using MyModule.Infrastructure;
                public class Controller
                {
                    public object Get(MyDb db) => {|#0:db.Widgets|};
                }
            }
            """;

        await new ShopFlowAnalyzerTest<RawDbSetAnalyzer>
        {
            TestCode = source,
            ExpectedDiagnostics =
            {
                new DiagnosticResult(RawDbSetAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                    .WithLocation(0)
                    .WithArguments("Widget", "Api"),
            },
        }.RunAsync();
    }

    [Fact]
    public async Task SetGeneric_FromInfrastructureRepositories_NotFlagged()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;

            namespace MyModule.Infrastructure.Repositories
            {
                public class WidgetRepository
                {
                    private readonly DbContext _db;
                    public WidgetRepository(DbContext db) { _db = db; }
                    public object Get() => _db.Set<Widget>();
                }

                public class Widget { public int Id { get; set; } }
            }
            """;

        await new ShopFlowAnalyzerTest<RawDbSetAnalyzer> { TestCode = source }.RunAsync();
    }

    [Fact]
    public async Task SetGeneric_FromDomainNamespace_NotFlagged()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;

            namespace MyModule.Domain
            {
                public class Helper
                {
                    private readonly DbContext _db;
                    public Helper(DbContext db) { _db = db; }
                    public object Get() => _db.Set<Widget>();
                }

                public class Widget { public int Id { get; set; } }
            }
            """;

        await new ShopFlowAnalyzerTest<RawDbSetAnalyzer> { TestCode = source }.RunAsync();
    }
}
