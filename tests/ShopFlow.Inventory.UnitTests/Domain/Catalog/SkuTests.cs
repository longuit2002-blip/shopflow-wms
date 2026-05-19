using ShopFlow.Inventory.Domain.Catalog;
using ShopFlow.Inventory.Domain.Catalog.ValueObjects;
using SkuCode = ShopFlow.Inventory.Domain.Sku;

namespace ShopFlow.Inventory.UnitTests.Domain.Catalog;

/// <summary>
/// Sprint-7.5 U3 — invariant + behavior tests for the rich SKU catalog
/// aggregate at <c>ShopFlow.Inventory.Domain.Catalog.Sku</c>. The
/// pre-existing <c>ShopFlow.Inventory.Domain.Sku</c> value object lives
/// at <c>tests/Domain/SkuTests.cs</c> (one folder up); the namespace
/// + folder split keeps the two distinguishable while letting the
/// new aggregate keep the natural type name.
/// </summary>
public sealed class SkuTests
{
    private static readonly SkuCode Code = SkuCode.Create("SKU-1");

    [Fact]
    public void Create_WithMinimalArgs_ProducesNewAggregate()
    {
        var result = Sku.Create(Code, "Test Product");

        result.IsSuccess.Should().BeTrue();
        var sku = result.Value!;
        sku.Code.Should().Be(Code);
        sku.Name.Should().Be("Test Product");
        sku.Category.Should().BeNull();
        sku.Threshold.Should().BeNull();
        sku.WeightGrams.Should().BeNull();
        sku.Dimensions.Should().BeNull();
        sku.Barcode.Should().BeNull();
        sku.Brand.Should().BeNull();
        sku.IsFlashSale.Should().BeFalse();
    }

    [Fact]
    public void Create_WithFullPayload_StoresEveryField()
    {
        var dims = SkuDimensions.Create(10m, 20m, 30m, "cm");

        var result = Sku.Create(
            code: Code,
            name: " Headphones ",
            category: " electronics ",
            threshold: 5,
            weightGrams: 200,
            dimensions: dims,
            description: " noise cancelling ",
            imageUrl: " https://cdn/p.jpg ",
            barcode: " 1234567890123 ",
            brand: " Sony ",
            isFlashSale: true
        );

        result.IsSuccess.Should().BeTrue();
        var sku = result.Value!;
        sku.Name.Should().Be("Headphones");
        sku.Category.Should().Be("electronics");
        sku.Threshold.Should().Be(5);
        sku.WeightGrams.Should().Be(200);
        sku.Dimensions.Should().Be(dims);
        sku.Description.Should().Be("noise cancelling");
        sku.ImageUrl.Should().Be("https://cdn/p.jpg");
        sku.Barcode.Should().Be("1234567890123");
        sku.Brand.Should().Be("Sony");
        sku.IsFlashSale.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankName_FailsWithCode(string name)
    {
        var result = Sku.Create(Code, name);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("sku.name_required");
    }

    [Fact]
    public void Create_NegativeThreshold_FailsWithCode()
    {
        var result = Sku.Create(Code, "x", threshold: -1);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("sku.threshold_negative");
    }

    [Fact]
    public void Create_NegativeWeight_FailsWithCode()
    {
        var result = Sku.Create(Code, "x", weightGrams: -1);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("sku.weight_negative");
    }

    [Fact]
    public void Create_NullDimensions_AcceptedAsOptional()
    {
        var result = Sku.Create(Code, "x", dimensions: null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Dimensions.Should().BeNull();
    }

    [Fact]
    public void UpdateFlashSale_FlipsState_ReturnsTrue()
    {
        var sku = Sku.Create(Code, "x").Value!;

        var changed = sku.UpdateFlashSale(true);

        changed.Should().BeTrue();
        sku.IsFlashSale.Should().BeTrue();
        sku.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void UpdateFlashSale_NoChange_ReturnsFalse()
    {
        var sku = Sku.Create(Code, "x", isFlashSale: true).Value!;

        var changed = sku.UpdateFlashSale(true);

        changed.Should().BeFalse();
        // The U5 outbox-emit seam reads this flag — a false return MUST
        // mean the row was untouched so retries do not double-publish.
        sku.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void UpdateThreshold_NewValue_UpdatesAndStamps()
    {
        var sku = Sku.Create(Code, "x").Value!;

        var result = sku.UpdateThreshold(7);

        result.IsSuccess.Should().BeTrue();
        sku.Threshold.Should().Be(7);
        sku.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void UpdateThreshold_SameValue_NoUpdatedAtStamp()
    {
        var sku = Sku.Create(Code, "x", threshold: 7).Value!;

        var result = sku.UpdateThreshold(7);

        result.IsSuccess.Should().BeTrue();
        sku.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void UpdateThreshold_Negative_FailsWithCode()
    {
        var sku = Sku.Create(Code, "x").Value!;

        var result = sku.UpdateThreshold(-1);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("sku.threshold_negative");
    }

    [Fact]
    public void UpdateMetadata_ChangedFields_ReturnsTrue()
    {
        var sku = Sku.Create(Code, "old").Value!;

        var result = sku.UpdateMetadata(
            name: "new",
            category: "electronics",
            threshold: null,
            weightGrams: null,
            dimensions: null,
            description: null,
            imageUrl: null,
            barcode: null,
            brand: null
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        sku.Name.Should().Be("new");
        sku.Category.Should().Be("electronics");
    }

    [Fact]
    public void UpdateMetadata_NoChanges_ReturnsFalse()
    {
        var sku = Sku.Create(Code, "x").Value!;

        var result = sku.UpdateMetadata(
            name: "x",
            category: null,
            threshold: null,
            weightGrams: null,
            dimensions: null,
            description: null,
            imageUrl: null,
            barcode: null,
            brand: null
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
        sku.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void UpdateMetadata_OverlongCategory_FailsWithCode()
    {
        var sku = Sku.Create(Code, "x").Value!;
        var overlong = new string('c', Sku.MaxCategoryLength + 1);

        var result = sku.UpdateMetadata(
            name: "x",
            category: overlong,
            threshold: null,
            weightGrams: null,
            dimensions: null,
            description: null,
            imageUrl: null,
            barcode: null,
            brand: null
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("sku.category_too_long");
    }
}
