using ShopFlow.Notification.Domain.ValueObjects;

namespace ShopFlow.Notification.UnitTests.ValueObjects;

public sealed class RecipientTests
{
    private static readonly Guid AnyTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Create_LowercasesAndTrimsEmail()
    {
        var recipient = Recipient.Create("  ALICE@Example.COM  ", "Alice", AnyTenant);

        recipient.Email.Should().Be("alice@example.com");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsNullOrEmptyEmail(string? email)
    {
        var act = () => Recipient.Create(email, "Alice", AnyTenant);

        act.Should().Throw<ArgumentException>().WithParameterName("email");
    }

    [Fact]
    public void Create_RejectsEmailWithoutAtSign()
    {
        var act = () => Recipient.Create("aliceexample.com", "Alice", AnyTenant);

        act.Should().Throw<ArgumentException>().WithParameterName("email");
    }

    [Fact]
    public void Create_RejectsEmailWithEmptyLocalPart()
    {
        var act = () => Recipient.Create("@example.com", "Alice", AnyTenant);

        act.Should().Throw<ArgumentException>().WithParameterName("email");
    }

    [Fact]
    public void Create_RejectsEmailExceeding254Octets()
    {
        var local = new string('a', 250);
        var oversized = local + "@x.io"; // > 254 octets total

        var act = () => Recipient.Create(oversized, "Alice", AnyTenant);

        act.Should().Throw<ArgumentException>().WithParameterName("email");
    }

    [Fact]
    public void Create_RejectsEmptyTenantId()
    {
        var act = () => Recipient.Create("alice@example.com", "Alice", Guid.Empty);

        act.Should().Throw<ArgumentException>().WithParameterName("tenantId");
    }

    [Fact]
    public void Create_AllowsNullDisplayName()
    {
        var recipient = Recipient.Create("alice@example.com", null, AnyTenant);

        recipient.DisplayName.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NormalisesEmptyDisplayNameToNull(string blankName)
    {
        var recipient = Recipient.Create("alice@example.com", blankName, AnyTenant);

        recipient.DisplayName.Should().BeNull();
    }

    [Fact]
    public void Create_TrimsDisplayName()
    {
        var recipient = Recipient.Create("alice@example.com", "  Alice  ", AnyTenant);

        recipient.DisplayName.Should().Be("Alice");
    }

    [Fact]
    public void Create_RetainsTenantId()
    {
        var recipient = Recipient.Create("alice@example.com", "Alice", AnyTenant);

        recipient.TenantId.Should().Be(AnyTenant);
    }
}
