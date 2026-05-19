using ShopFlow.Inventory.Domain.Catalog.ValueObjects;

namespace ShopFlow.Inventory.UnitTests.Domain.Catalog;

public sealed class SkuDimensionsTests
{
    [Fact]
    public void Create_ValidArgs_StoresFields()
    {
        var d = SkuDimensions.Create(10m, 20m, 30m, "cm");

        d.Length.Should().Be(10m);
        d.Width.Should().Be(20m);
        d.Height.Should().Be(30m);
        d.Unit.Should().Be("cm");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_NonPositiveLength_Throws(decimal length)
    {
        var act = () => SkuDimensions.Create(length, 1m, 1m, "cm");

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("length");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_NonPositiveWidth_Throws(decimal width)
    {
        var act = () => SkuDimensions.Create(1m, width, 1m, "cm");

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("width");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_NonPositiveHeight_Throws(decimal height)
    {
        var act = () => SkuDimensions.Create(1m, 1m, height, "cm");

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("height");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankUnit_Throws(string unit)
    {
        var act = () => SkuDimensions.Create(1m, 1m, 1m, unit);

        act.Should().Throw<ArgumentException>().WithParameterName("unit");
    }

    [Fact]
    public void Create_OverlongUnit_Throws()
    {
        var overlong = new string('u', SkuDimensions.MaxUnitLength + 1);

        var act = () => SkuDimensions.Create(1m, 1m, 1m, overlong);

        act.Should().Throw<ArgumentException>().WithParameterName("unit");
    }

    [Fact]
    public void Equality_IsStructuralOverComponents()
    {
        var a = SkuDimensions.Create(1m, 2m, 3m, "cm");
        var b = SkuDimensions.Create(1m, 2m, 3m, "cm");
        var c = SkuDimensions.Create(1m, 2m, 3m, "mm");

        a.Should().Be(b);
        a.Should().NotBe(c);
    }
}
