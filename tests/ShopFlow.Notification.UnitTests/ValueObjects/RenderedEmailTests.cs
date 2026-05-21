using ShopFlow.Notification.Domain.ValueObjects;

namespace ShopFlow.Notification.UnitTests.ValueObjects;

public sealed class RenderedEmailTests
{
    private static readonly Guid AnyEvent = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Create_HappyPath_RetainsAllFields()
    {
        var email = RenderedEmail.Create("Reset your password", "plain", "<p>html</p>", AnyEvent);

        email.Subject.Should().Be("Reset your password");
        email.BodyText.Should().Be("plain");
        email.BodyHtml.Should().Be("<p>html</p>");
        email.SourceEventId.Should().Be(AnyEvent);
    }

    [Fact]
    public void Create_TrimsSubjectWhitespace()
    {
        var email = RenderedEmail.Create("  Hello  ", "body", "<p/>", AnyEvent);

        email.Subject.Should().Be("Hello");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void Create_RejectsNullOrEmptySubject(string? subject)
    {
        var act = () => RenderedEmail.Create(subject, "body", "<p/>", AnyEvent);

        act.Should().Throw<ArgumentException>().WithParameterName("subject");
    }

    [Fact]
    public void Create_RejectsSubjectExceeding998Octets()
    {
        var oversized = new string('x', 999);

        var act = () => RenderedEmail.Create(oversized, "body", "<p/>", AnyEvent);

        act.Should().Throw<ArgumentException>().WithParameterName("subject");
    }

    [Fact]
    public void Create_AcceptsSubjectAt998Octets()
    {
        var boundary = new string('x', 998);

        var act = () => RenderedEmail.Create(boundary, "body", "<p/>", AnyEvent);

        act.Should().NotThrow();
    }

    [Fact]
    public void Create_RejectsNullBodyText()
    {
        var act = () => RenderedEmail.Create("Hello", null, "<p/>", AnyEvent);

        act.Should().Throw<ArgumentException>().WithParameterName("bodyText");
    }

    [Fact]
    public void Create_RejectsNullBodyHtml()
    {
        var act = () => RenderedEmail.Create("Hello", "body", null, AnyEvent);

        act.Should().Throw<ArgumentException>().WithParameterName("bodyHtml");
    }

    [Fact]
    public void Create_AllowsEmptyStringForBodyText()
    {
        var email = RenderedEmail.Create("Hello", string.Empty, "<p/>", AnyEvent);

        email.BodyText.Should().Be(string.Empty);
    }

    [Fact]
    public void Create_AllowsEmptyStringForBodyHtml()
    {
        var email = RenderedEmail.Create("Hello", "body", string.Empty, AnyEvent);

        email.BodyHtml.Should().Be(string.Empty);
    }

    [Fact]
    public void Create_RejectsEmptySourceEventId()
    {
        var act = () => RenderedEmail.Create("Hello", "body", "<p/>", Guid.Empty);

        act.Should().Throw<ArgumentException>().WithParameterName("sourceEventId");
    }
}
