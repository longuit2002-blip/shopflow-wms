using ShopFlow.StockSync.Domain.Aggregates;

namespace ShopFlow.StockSync.UnitTests.Aggregates;

public sealed class SkuFlagTests
{
    [Fact]
    public void Create_WithValidSku_SetsFields()
    {
        var flag = SkuFlag.Create("SKU-X", isFlashSale: true);

        flag.Sku.Should().Be("SKU-X");
        flag.IsFlashSale.Should().BeTrue();
        flag.UpdatedAt.Should().BeNull();
        flag.CreatedAt.Should().BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact]
    public void Create_DefaultsToNotFlashSale_WhenFalsePassed()
    {
        var flag = SkuFlag.Create("SKU-Y", isFlashSale: false);

        flag.IsFlashSale.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptySku_Throws(string? sku)
    {
        Action act = () => SkuFlag.Create(sku!, isFlashSale: true);

        act.Should().Throw<ArgumentException>().And.ParamName.Should().Be("sku");
    }

    [Fact]
    public void SetFlashSale_FlipsValueAndAdvancesUpdatedAt()
    {
        var flag = SkuFlag.Create("SKU-X", isFlashSale: false);

        flag.SetFlashSale(true);

        flag.IsFlashSale.Should().BeTrue();
        flag.UpdatedAt.Should().NotBeNull();
        flag.UpdatedAt!.Value.Should().BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact]
    public void SetFlashSale_IsIdempotent_WhenSameValue()
    {
        var flag = SkuFlag.Create("SKU-X", isFlashSale: true);

        flag.SetFlashSale(true);

        flag.UpdatedAt.Should().BeNull(
            "idempotent set must not advance UpdatedAt when value is unchanged"
        );
    }

    [Fact]
    public void SetFlashSale_ToggleOff_AdvancesUpdatedAt()
    {
        var flag = SkuFlag.Create("SKU-X", isFlashSale: true);

        flag.SetFlashSale(false);

        flag.IsFlashSale.Should().BeFalse();
        flag.UpdatedAt.Should().NotBeNull();
    }
}
