using NSubstitute;
using ShopFlow.Channel.Application.Ports;
using ShopFlow.Channel.Domain.ProductMappings;
using ShopFlow.Channel.Infrastructure.Mapping;

namespace ShopFlow.Channel.UnitTests;

/// <summary>
/// Sprint-4 plan U6 — three-tier mapping service coverage. Levenshtein
/// scoring math + Exact-priority + fuzzy-threshold behaviour. Repository
/// is mocked; integration tests exercise the EF + Postgres roundtrip.
/// </summary>
public sealed class HybridProductMappingServiceTests
{
    private static readonly Guid ChannelId = Guid.NewGuid();

    private static ProductMapping NewMapping(string ext, string @internal, MappingMethod method)
    {
        return ProductMapping
            .Create(
                ChannelId,
                ExternalSku.Create(ext).Value!,
                @internal,
                method,
                method == MappingMethod.Fuzzy ? 0.8m : 1m
            )
            .Value!;
    }

    [Fact]
    public async Task ResolveAsync_ExactMatch_ReturnsExact()
    {
        var repo = Substitute.For<IProductMappingRepository>();
        var existing = NewMapping("sku-001", "INT-001", MappingMethod.Exact);
        repo.FindExactAsync(ChannelId, Arg.Any<ExternalSku>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var sut = new HybridProductMappingService(repo);

        var result = await sut.ResolveAsync(ChannelId, "sku-001", default);

        result.Should().NotBeNull();
        result!.InternalSku.Should().Be("INT-001");
        result.Method.Should().Be(MappingMethod.Exact);
        result.Confidence.Should().Be(1m);

        await repo.DidNotReceive().ReadAllByChannelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_NoExact_FuzzyAboveThreshold_ReturnsFuzzy()
    {
        var repo = Substitute.For<IProductMappingRepository>();
        repo.FindExactAsync(Arg.Any<Guid>(), Arg.Any<ExternalSku>(), Arg.Any<CancellationToken>())
            .Returns((ProductMapping?)null);
        repo.ReadAllByChannelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                NewMapping("sku-001", "INT-001", MappingMethod.Manual),
                NewMapping("sku-002", "INT-002", MappingMethod.Manual),
            });

        var sut = new HybridProductMappingService(repo);

        // "sku-0O1" (zero -> letter O) is 1 edit away from "sku-001" — score
        // = 1 - (1/7) ≈ 0.857, well above 0.6.
        var result = await sut.ResolveAsync(ChannelId, "sku-0O1", default);

        result.Should().NotBeNull();
        result!.InternalSku.Should().Be("INT-001");
        result.Method.Should().Be(MappingMethod.Fuzzy);
        result.Confidence.Should().BeGreaterThan(0.7m).And.BeLessThan(1m);
    }

    [Fact]
    public async Task ResolveAsync_FuzzyBelowThreshold_ReturnsNull()
    {
        var repo = Substitute.For<IProductMappingRepository>();
        repo.FindExactAsync(Arg.Any<Guid>(), Arg.Any<ExternalSku>(), Arg.Any<CancellationToken>())
            .Returns((ProductMapping?)null);
        repo.ReadAllByChannelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new[] { NewMapping("totally-different", "INT-X", MappingMethod.Manual) });

        var sut = new HybridProductMappingService(repo);

        var result = await sut.ResolveAsync(ChannelId, "sku-001", default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_EmptyCatalogue_ReturnsNull()
    {
        var repo = Substitute.For<IProductMappingRepository>();
        repo.FindExactAsync(Arg.Any<Guid>(), Arg.Any<ExternalSku>(), Arg.Any<CancellationToken>())
            .Returns((ProductMapping?)null);
        repo.ReadAllByChannelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProductMapping>());

        var sut = new HybridProductMappingService(repo);

        var result = await sut.ResolveAsync(ChannelId, "sku-001", default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_BlankSku_ReturnsNull()
    {
        var repo = Substitute.For<IProductMappingRepository>();
        var sut = new HybridProductMappingService(repo);

        (await sut.ResolveAsync(ChannelId, "", default)).Should().BeNull();
        (await sut.ResolveAsync(ChannelId, "   ", default)).Should().BeNull();
        await repo.DidNotReceive().FindExactAsync(Arg.Any<Guid>(), Arg.Any<ExternalSku>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SimilarityScore_IdenticalIgnoresCase()
    {
        HybridProductMappingService.SimilarityScore("sku-001", "SKU-001").Should().Be(1m);
    }

    [Fact]
    public void SimilarityScore_EmptyInputs_ReturnsZero()
    {
        HybridProductMappingService.SimilarityScore("", "abc").Should().Be(0m);
        HybridProductMappingService.SimilarityScore("abc", "").Should().Be(0m);
    }

    [Fact]
    public void Levenshtein_StandardCases()
    {
        HybridProductMappingService.Levenshtein("kitten", "sitting").Should().Be(3);
        HybridProductMappingService.Levenshtein("abc", "abc").Should().Be(0);
        HybridProductMappingService.Levenshtein("abc", "abd").Should().Be(1);
        HybridProductMappingService.Levenshtein("", "abc").Should().Be(3);
    }
}
