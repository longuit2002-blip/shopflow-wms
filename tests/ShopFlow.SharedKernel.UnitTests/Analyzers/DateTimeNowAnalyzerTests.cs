using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using ShopFlow.SharedKernel.Analyzers;
using Xunit;

namespace ShopFlow.SharedKernel.UnitTests.Analyzers;

public class DateTimeNowAnalyzerTests
{
    [Fact]
    public async Task DateTimeNow_Flagged()
    {
        const string source = """
            using System;

            public class Sample
            {
                public DateTime Get() => {|#0:DateTime.Now|};
            }
            """;

        await new ShopFlowAnalyzerTest<DateTimeNowAnalyzer>
        {
            TestCode = source,
            ExpectedDiagnostics =
            {
                new DiagnosticResult(DateTimeNowAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                    .WithLocation(0)
                    .WithArguments("DateTime.Now"),
            },
        }.RunAsync();
    }

    [Fact]
    public async Task DateTimeToday_Flagged()
    {
        const string source = """
            using System;

            public class Sample
            {
                public DateTime Get() => {|#0:DateTime.Today|};
            }
            """;

        await new ShopFlowAnalyzerTest<DateTimeNowAnalyzer>
        {
            TestCode = source,
            ExpectedDiagnostics =
            {
                new DiagnosticResult(DateTimeNowAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                    .WithLocation(0)
                    .WithArguments("DateTime.Today"),
            },
        }.RunAsync();
    }

    [Fact]
    public async Task DateTimeOffsetNow_Flagged()
    {
        const string source = """
            using System;

            public class Sample
            {
                public DateTimeOffset Get() => {|#0:DateTimeOffset.Now|};
            }
            """;

        await new ShopFlowAnalyzerTest<DateTimeNowAnalyzer>
        {
            TestCode = source,
            ExpectedDiagnostics =
            {
                new DiagnosticResult(DateTimeNowAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                    .WithLocation(0)
                    .WithArguments("DateTimeOffset.Now"),
            },
        }.RunAsync();
    }

    [Fact]
    public async Task DateTimeUtcNow_NotFlagged()
    {
        const string source = """
            using System;

            public class Sample
            {
                public DateTime Get() => DateTime.UtcNow;
                public DateTimeOffset GetOffset() => DateTimeOffset.UtcNow;
            }
            """;

        await new ShopFlowAnalyzerTest<DateTimeNowAnalyzer> { TestCode = source }.RunAsync();
    }
}
