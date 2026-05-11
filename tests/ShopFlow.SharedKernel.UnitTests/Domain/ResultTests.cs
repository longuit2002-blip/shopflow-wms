using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.SharedKernel.UnitTests.Domain;

public class ResultTests
{
    [Fact]
    public void Success_returns_value_and_no_error()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Failure_returns_error_and_no_value()
    {
        var result = Result<int>.Failure("nope", "ERR1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("nope");
        result.ErrorCode.Should().Be("ERR1");
    }

    [Fact]
    public void Match_invokes_success_branch_on_success()
    {
        var result = Result<string>.Success("hello");

        var outcome = result.Match(v => $"got: {v}", _ => "fallback");

        outcome.Should().Be("got: hello");
    }
}
