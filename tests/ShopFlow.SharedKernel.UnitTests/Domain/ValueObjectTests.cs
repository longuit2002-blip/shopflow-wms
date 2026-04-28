using FluentAssertions;
using ShopFlow.SharedKernel.Domain;
using Xunit;

namespace ShopFlow.SharedKernel.UnitTests.Domain;

public class ValueObjectTests
{
    [Fact]
    public void Equality_IsStructural_AcrossSameComponents()
    {
        var a = new Money(100m, "USD");
        var b = new Money(100m, "USD");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equality_DiffersAcrossDifferentComponents()
    {
        var a = new Money(100m, "USD");
        var b = new Money(101m, "USD");

        a.Should().NotBe(b);
        (a == b).Should().BeFalse();
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void Equality_DiffersAcrossSubtypes_EvenWhenComponentsMatch()
    {
        var a = new Money(100m, "USD");
        var b = new SpecialMoney(100m, "USD");

        a.Equals(b).Should().BeFalse();
    }

    private sealed class Money : ValueObject
    {
        public Money(decimal amount, string currency)
        {
            Amount = amount;
            Currency = currency;
        }

        public decimal Amount { get; }
        public string Currency { get; }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }
    }

    private sealed class SpecialMoney : ValueObject
    {
        public SpecialMoney(decimal amount, string currency)
        {
            Amount = amount;
            Currency = currency;
        }

        public decimal Amount { get; }
        public string Currency { get; }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }
    }
}
