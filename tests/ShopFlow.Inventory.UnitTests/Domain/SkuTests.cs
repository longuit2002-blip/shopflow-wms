using FluentAssertions;
using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.UnitTests.Domain;

public sealed class SkuTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Constructor_RejectsNullOrWhitespace(string? value)
    {
        var act = () => new Sku(value!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RejectsValuesOverMaxLength()
    {
        var tooLong = new string('a', Sku.MaxLength + 1);
        var act = () => new Sku(tooLong);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_AcceptsAtMaxLength()
    {
        var atMax = new string('a', Sku.MaxLength);
        var sku = new Sku(atMax);
        sku.Value.Should().Be(atMax);
    }

    [Fact]
    public void Equality_IsStructural()
    {
        var a = new Sku("SKU-001");
        var b = new Sku("SKU-001");
        var c = new Sku("SKU-002");

        a.Should().Be(b);
        a.Should().NotBe(c);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
