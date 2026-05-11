using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.SharedKernel.UnitTests.Domain;

public class ValueObjectTests
{
    private sealed class Money : ValueObject
    {
        public decimal Amount { get; }
        public string Currency { get; }

        public Money(decimal amount, string currency)
        {
            Amount = amount;
            Currency = currency;
        }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }
    }

    [Fact]
    public void Equals_returns_true_for_equal_components()
    {
        var a = new Money(10m, "USD");
        var b = new Money(10m, "USD");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Equals_returns_false_when_any_component_differs()
    {
        var a = new Money(10m, "USD");
        var b = new Money(10m, "SGD");

        a.Should().NotBe(b);
        (a != b).Should().BeTrue();
    }
}
