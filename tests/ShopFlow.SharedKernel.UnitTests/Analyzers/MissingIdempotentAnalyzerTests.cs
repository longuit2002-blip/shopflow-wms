using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using ShopFlow.SharedKernel.Analyzers;
using Xunit;

namespace ShopFlow.SharedKernel.UnitTests.Analyzers;

public class MissingIdempotentAnalyzerTests
{
    [Fact]
    public async Task WebhookPostByName_WithoutIdempotent_Flagged()
    {
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            public class WebhookController : ControllerBase
            {
                [HttpPost("/api/webhook/shopee")]
                public IActionResult {|#0:WebhookReceive|}([FromBody] object payload)
                {
                    return Ok();
                }
            }
            """;

        await new ShopFlowAnalyzerTest<MissingIdempotentAnalyzer>
        {
            TestCode = source,
            ExpectedDiagnostics =
            {
                new DiagnosticResult(
                    MissingIdempotentAnalyzer.DiagnosticId,
                    DiagnosticSeverity.Warning
                )
                    .WithLocation(0)
                    .WithArguments("WebhookReceive"),
            },
        }.RunAsync();
    }

    [Fact]
    public async Task WebhookByRouteTemplate_WithoutIdempotent_Flagged()
    {
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            public class ShopeeController : ControllerBase
            {
                [HttpPost("/api/webhook/order")]
                public IActionResult {|#0:HandleOrder|}([FromBody] object payload)
                {
                    return Ok();
                }
            }
            """;

        await new ShopFlowAnalyzerTest<MissingIdempotentAnalyzer>
        {
            TestCode = source,
            ExpectedDiagnostics =
            {
                new DiagnosticResult(
                    MissingIdempotentAnalyzer.DiagnosticId,
                    DiagnosticSeverity.Warning
                )
                    .WithLocation(0)
                    .WithArguments("HandleOrder"),
            },
        }.RunAsync();
    }

    [Fact]
    public async Task WebhookWithIdempotentAttribute_NotFlagged()
    {
        const string source = """
            using Microsoft.AspNetCore.Mvc;
            using ShopFlow.SharedKernel.Application.Attributes;

            public class WebhookController : ControllerBase
            {
                [HttpPost("/api/webhook/shopee")]
                [Idempotent]
                public IActionResult WebhookReceive([FromBody] object payload)
                {
                    return Ok();
                }
            }
            """;

        await new ShopFlowAnalyzerTest<MissingIdempotentAnalyzer> { TestCode = source }.RunAsync();
    }

    [Fact]
    public async Task NonWebhookHttpPost_NotFlagged()
    {
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            public class OrdersController : ControllerBase
            {
                [HttpPost("/api/orders")]
                public IActionResult Create([FromBody] object payload) => Ok();
            }
            """;

        await new ShopFlowAnalyzerTest<MissingIdempotentAnalyzer> { TestCode = source }.RunAsync();
    }
}
