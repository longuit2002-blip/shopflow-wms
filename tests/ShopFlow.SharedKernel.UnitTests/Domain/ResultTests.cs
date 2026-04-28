using FluentAssertions;
using ShopFlow.SharedKernel.Domain;
using Xunit;

namespace ShopFlow.SharedKernel.UnitTests.Domain;

public class ResultTests
{
    [Fact]
    public void Success_CarriesValue_AndIsSuccessIsTrue()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Error.Should().BeNull();
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void Failure_CarriesError_AndIsSuccessIsFalse()
    {
        var result = Result<int>.Failure("oversold", "INVENTORY_OVERSOLD");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("oversold");
        result.ErrorCode.Should().Be("INVENTORY_OVERSOLD");
    }

    [Fact]
    public void Match_RoutesToOnSuccess_WhenSuccess()
    {
        var result = Result<string>.Success("ok");

        var matched = result.Match(onSuccess: v => $"hit:{v}", onFailure: e => $"miss:{e}");

        matched.Should().Be("hit:ok");
    }

    [Fact]
    public void Match_RoutesToOnFailure_WhenFailure()
    {
        var result = Result<string>.Failure("boom");

        var matched = result.Match(onSuccess: v => $"hit:{v}", onFailure: e => $"miss:{e}");

        matched.Should().Be("miss:boom");
    }

    [Fact]
    public void NonGenericResult_Success_HasNoErrorPayload()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.Error.Should().BeNull();
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void NonGenericResult_Failure_CarriesErrorAndCode()
    {
        var result = Result.Failure("bad input", "VALIDATION");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("bad input");
        result.ErrorCode.Should().Be("VALIDATION");
    }
}
