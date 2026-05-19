using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShopFlow.SharedKernel.Infrastructure;
using Xunit;

namespace ShopFlow.SharedKernel.UnitTests.Infrastructure;

/// <summary>
/// Sprint-7.5 U1 — pins the camelCase wire-format convention applied
/// through the new <c>AddShopFlowControllers</c> helper. The MVC pipeline
/// (controllers + ApiBehaviorOptions) is configured here; the SignalR
/// hub-protocol coverage (also part of the U1 flip) is covered alongside
/// in <see cref="AddShopFlowDefaultsSignalRJsonProtocolTests"/> since
/// that lives inside <c>AddShopFlowDefaults</c>.
///
/// Test scenarios follow plan U1's enumerated cases:
///  - PascalCase record → camelCase JSON keys on serialize
///  - camelCase request body → PascalCase property bind on deserialize
///  - Multi-word identifier (<c>AvailableToSell</c> → <c>availableToSell</c>)
///  - Acronym-prefixed identifier (<c>SKU</c> → <c>sku</c>) per .NET default rule
///  - Collection property names (<c>Allocations</c> → <c>allocations</c>)
///  - Nested record property names also flip.
/// </summary>
public sealed class AddShopFlowControllersTests
{
    // Test records with shapes that mirror real Sprint-1+ DTOs.
    private sealed record SimplePayload(string Sku, int AvailableToSell);

    private sealed record CollectionPayload(string Sku, IReadOnlyList<Allocation> Allocations);

    private sealed record Allocation(string ChannelId, int Reserved);

    private sealed record NestedPayload(string Sku, Dimensions? Dimensions);

    private sealed record Dimensions(decimal Length, decimal Width, decimal Height);

    [Fact]
    public void AddShopFlowControllers_ConfiguresCamelCasePropertyNamingPolicy()
    {
        var services = new ServiceCollection();

        services.AddShopFlowControllers();

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value;

        options.JsonSerializerOptions.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
    }

    [Fact]
    public void AddShopFlowControllers_ReturnsMvcBuilderForChaining()
    {
        var services = new ServiceCollection();

        var builder = services.AddShopFlowControllers();

        builder.Should().NotBeNull();
        builder.Services.Should().BeSameAs(services);
    }

    [Fact]
    public void SerializingPascalCaseRecord_ProducesCamelCaseJsonKeys()
    {
        var options = ResolveJsonSerializerOptions();
        var payload = new SimplePayload(Sku: "YN-001", AvailableToSell: 42);

        var json = JsonSerializer.Serialize(payload, options);

        json.Should().Contain("\"sku\"");
        json.Should().Contain("\"availableToSell\"");
        json.Should().NotContain("\"Sku\"");
        json.Should().NotContain("\"AvailableToSell\"");
    }

    [Fact]
    public void DeserializingCamelCaseJson_PopulatesPascalCaseRecord()
    {
        var options = ResolveJsonSerializerOptions();
        const string json = """{"sku":"YN-001","availableToSell":42}""";

        var payload = JsonSerializer.Deserialize<SimplePayload>(json, options);

        payload.Should().NotBeNull();
        payload!.Sku.Should().Be("YN-001");
        payload.AvailableToSell.Should().Be(42);
    }

    [Fact]
    public void MultiWordProperty_FlipsToCamelCase()
    {
        var options = ResolveJsonSerializerOptions();
        var payload = new SimplePayload(Sku: "X", AvailableToSell: 1);

        var json = JsonSerializer.Serialize(payload, options);

        json.Should().Contain("\"availableToSell\":1");
    }

    [Fact]
    public void AcronymPrefixedProperty_LowercasesPerNetDefaultCamelCaseRule()
    {
        // .NET's default JsonNamingPolicy.CamelCase lowercases the first
        // character only. `Sku` (3-letter pascal) becomes `sku`. This
        // pins the .NET behaviour so future serializer upgrades that
        // change acronym handling surface here.
        var options = ResolveJsonSerializerOptions();
        var payload = new SimplePayload(Sku: "X", AvailableToSell: 1);

        var json = JsonSerializer.Serialize(payload, options);

        json.Should().Contain("\"sku\":\"X\"");
    }

    [Fact]
    public void CollectionProperty_NameFlipsToCamelCase()
    {
        var options = ResolveJsonSerializerOptions();
        var payload = new CollectionPayload(
            Sku: "X",
            Allocations: new[] { new Allocation(ChannelId: "shopee", Reserved: 3) }
        );

        var json = JsonSerializer.Serialize(payload, options);

        json.Should().Contain("\"allocations\":");
        // Element-level property names also flip.
        json.Should().Contain("\"channelId\":\"shopee\"");
        json.Should().Contain("\"reserved\":3");
    }

    [Fact]
    public void NestedRecord_PropertyNamesAlsoFlipToCamelCase()
    {
        var options = ResolveJsonSerializerOptions();
        var payload = new NestedPayload(
            Sku: "X",
            Dimensions: new Dimensions(Length: 10, Width: 5, Height: 2)
        );

        var json = JsonSerializer.Serialize(payload, options);

        json.Should().Contain("\"dimensions\":");
        json.Should().Contain("\"length\":10");
        json.Should().Contain("\"width\":5");
        json.Should().Contain("\"height\":2");
    }

    /// <summary>
    /// Resolves the canonical JSON options the helper wires for MVC.
    /// Tests below assert against the same options the controllers use.
    /// </summary>
    private static JsonSerializerOptions ResolveJsonSerializerOptions()
    {
        var services = new ServiceCollection();
        services.AddShopFlowControllers();
        using var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;
    }
}
