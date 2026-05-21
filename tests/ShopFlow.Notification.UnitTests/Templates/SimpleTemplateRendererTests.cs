using ShopFlow.Notification.Application.Ports;
using ShopFlow.Notification.Infrastructure.Templates;

namespace ShopFlow.Notification.UnitTests.Templates;

public sealed class SimpleTemplateRendererTests
{
    private readonly SimpleTemplateRenderer _renderer = new();

    [Fact]
    public void RenderText_HappyPath_SubstitutesAllPlaceholders()
    {
        var template = "Hello {name}, your link: {url}";
        var vars = new Dictionary<string, string>
        {
            ["name"] = "Alice",
            ["url"] = "https://x.com",
        };

        var rendered = _renderer.RenderText(template, vars);

        rendered.Should().Be("Hello Alice, your link: https://x.com");
    }

    [Fact]
    public void RenderText_MissingKey_ThrowsWithMissingKeyName()
    {
        var template = "Hello {name}, your link: {url}";
        var vars = new Dictionary<string, string> { ["name"] = "Alice" };

        var act = () => _renderer.RenderText(template, vars);

        act.Should()
            .Throw<TemplateRenderException>()
            .Where(ex => ex.MissingKey == "url" && ex.Message.Contains("url", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderHtml_EscapesScriptInjectionViaDisplayName()
    {
        var template = "<p>Hello {name}, welcome!</p>";
        var vars = new Dictionary<string, string> { ["name"] = "<script>alert(1)</script>" };

        var rendered = _renderer.RenderHtml(template, vars);

        rendered
            .Should()
            .Be("<p>Hello &lt;script&gt;alert(1)&lt;/script&gt;, welcome!</p>");
    }

    [Fact]
    public void RenderText_DoesNotEscapeAngleBrackets()
    {
        var template = "Hello {name}";
        var vars = new Dictionary<string, string> { ["name"] = "<Alice>" };

        var rendered = _renderer.RenderText(template, vars);

        rendered.Should().Be("Hello <Alice>");
    }

    [Fact]
    public void RenderText_UnbalancedOpeningBrace_TreatsRemainderAsLiteral()
    {
        var template = "Hello {name} and {also-unclosed";
        var vars = new Dictionary<string, string> { ["name"] = "Alice" };

        var rendered = _renderer.RenderText(template, vars);

        rendered.Should().Be("Hello Alice and {also-unclosed");
    }

    [Fact]
    public void RenderText_AdjacentPlaceholders_SubstituteEachIndependently()
    {
        var template = "{a}{b}{c}";
        var vars = new Dictionary<string, string>
        {
            ["a"] = "1",
            ["b"] = "2",
            ["c"] = "3",
        };

        var rendered = _renderer.RenderText(template, vars);

        rendered.Should().Be("123");
    }

    [Fact]
    public void RenderText_EmptyTemplate_ReturnsEmptyString()
    {
        var rendered = _renderer.RenderText(string.Empty, new Dictionary<string, string>());

        rendered.Should().Be(string.Empty);
    }

    [Fact]
    public void RenderText_TemplateWithoutPlaceholders_ReturnsLiteralBody()
    {
        var rendered = _renderer.RenderText(
            "no placeholders here",
            new Dictionary<string, string>()
        );

        rendered.Should().Be("no placeholders here");
    }

    [Fact]
    public void RenderHtml_OnlyHtmlVariantEscapes()
    {
        var template = "{val}";
        var vars = new Dictionary<string, string> { ["val"] = "<b>" };

        _renderer.RenderText(template, vars).Should().Be("<b>");
        _renderer.RenderHtml(template, vars).Should().Be("&lt;b&gt;");
    }
}
