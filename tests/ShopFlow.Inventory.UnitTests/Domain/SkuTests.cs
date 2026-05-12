using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.UnitTests.Domain;

public sealed class SkuTests
{
    [Fact]
    public void Create_NonEmptyValue_TrimsAndStores()
    {
        var sku = Sku.Create("  ABC-123  ");

        sku.Value.Should().Be("ABC-123");
        sku.ToString().Should().Be("ABC-123");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankValue_Throws(string value)
    {
        var act = () => Sku.Create(value);
        act.Should().Throw<ArgumentException>().WithParameterName("value");
    }

    [Fact]
    public void Create_OverlongValue_Throws()
    {
        var value = new string('x', Sku.MaxLength + 1);

        var act = () => Sku.Create(value);

        act.Should().Throw<ArgumentException>().WithParameterName("value");
    }

    [Fact]
    public void Equality_IsCaseSensitiveAndStructural()
    {
        Sku.Create("ABC").Should().Be(Sku.Create("ABC"));
        Sku.Create("ABC").Should().NotBe(Sku.Create("abc"));
        (Sku.Create("ABC") == Sku.Create("ABC")).Should().BeTrue();
    }
}
