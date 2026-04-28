using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using ShopFlow.SharedKernel.Analyzers;
using Xunit;

namespace ShopFlow.SharedKernel.UnitTests.Analyzers;

public class MissingCorrelationAnalyzerTests
{
    [Fact]
    public async Task PublishWithoutRequestContext_Flagged()
    {
        const string source = """
            using System.Threading.Tasks;
            using MassTransit;

            public class Handler
            {
                private readonly IPublishEndpoint _bus;
                public Handler(IPublishEndpoint bus) { _bus = bus; }

                public Task Run() => {|#0:_bus.Publish(new MyEvent())|};
            }

            public record MyEvent();
            """;

        await new ShopFlowAnalyzerTest<MissingCorrelationAnalyzer>
        {
            TestCode = source,
            ExpectedDiagnostics =
            {
                new DiagnosticResult(
                    MissingCorrelationAnalyzer.DiagnosticId,
                    DiagnosticSeverity.Warning
                )
                    .WithLocation(0)
                    .WithArguments("IPublishEndpoint.Publish"),
            },
        }.RunAsync();
    }

    [Fact]
    public async Task PublishWithIRequestContextOnConstructor_NotFlagged()
    {
        const string source = """
            using System.Threading.Tasks;
            using MassTransit;
            using ShopFlow.SharedKernel.Application;

            public class Handler
            {
                private readonly IPublishEndpoint _bus;
                private readonly IRequestContext _ctx;
                public Handler(IPublishEndpoint bus, IRequestContext ctx) { _bus = bus; _ctx = ctx; }

                public Task Run() => _bus.Publish(new MyEvent());
            }

            public record MyEvent();
            """;

        await new ShopFlowAnalyzerTest<MissingCorrelationAnalyzer> { TestCode = source }.RunAsync();
    }

    [Fact]
    public async Task PublishWithIRequestContextOnMethod_NotFlagged()
    {
        const string source = """
            using System.Threading.Tasks;
            using MassTransit;
            using ShopFlow.SharedKernel.Application;

            public class Handler
            {
                public Task Run(IPublishEndpoint bus, IRequestContext ctx)
                    => bus.Publish(new MyEvent());
            }

            public record MyEvent();
            """;

        await new ShopFlowAnalyzerTest<MissingCorrelationAnalyzer> { TestCode = source }.RunAsync();
    }
}
