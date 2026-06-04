using Equibles.InsiderTrading.Data.Models;

namespace Equibles.UnitTests.InsiderTrading.Models;

public class InsiderSecurityClassificationTests
{
    // IsShareTransaction keeps derivative rows out of the dashboard value boards:
    // a derivative's PricePerShare is the instrument's own price, so Shares × Price
    // is not a transaction dollar value. The authoritative signal is SecurityKind;
    // only Unknown (not-yet-reclassified) rows fall back to the title keywords.
    private static readonly Func<InsiderTransaction, bool> IsShareTransaction =
        InsiderSecurityClassification.IsShareTransaction.Compile();

    private static InsiderTransaction Row(InsiderSecurityKind kind, string title) =>
        new() { SecurityKind = kind, SecurityTitle = title };

    [Fact]
    public void IsShareTransaction_NonDerivativeKind_IsTrue()
    {
        IsShareTransaction(Row(InsiderSecurityKind.NonDerivative, "Common Stock"))
            .Should()
            .BeTrue();
    }

    [Fact]
    public void IsShareTransaction_DerivativeKind_IsFalseEvenWithShareTitle()
    {
        // Authoritative kind wins over the title — a Derivative row is excluded
        // regardless of what its title says.
        IsShareTransaction(Row(InsiderSecurityKind.Derivative, "Common Stock"))
            .Should()
            .BeFalse();
    }

    [Fact]
    public void IsShareTransaction_UnknownWithPlainShareTitle_IsTrue()
    {
        IsShareTransaction(Row(InsiderSecurityKind.Unknown, "Common Stock")).Should().BeTrue();
    }

    [Fact]
    public void IsShareTransaction_UnknownWithNullTitle_IsTrue()
    {
        IsShareTransaction(Row(InsiderSecurityKind.Unknown, null)).Should().BeTrue();
    }

    [Theory]
    [InlineData("Pre-Funded Warrant (right to buy)")]
    [InlineData("Convertible Note")]
    [InlineData("Call option (obligation to sell)")]
    [InlineData("Put Option")]
    public void IsShareTransaction_UnknownWithDerivativeTitle_IsFalse(string title)
    {
        IsShareTransaction(Row(InsiderSecurityKind.Unknown, title)).Should().BeFalse();
    }

    [Fact]
    public void IsDerivativeTitle_PlainShareTitle_IsFalse()
    {
        InsiderSecurityClassification.IsDerivativeTitle("Common Stock").Should().BeFalse();
    }

    [Fact]
    public void IsDerivativeTitle_OptionTitle_IsTrue()
    {
        InsiderSecurityClassification
            .IsDerivativeTitle("Stock Option (Right to Buy)")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void IsDerivativeTitle_NullOrBlank_IsFalse()
    {
        InsiderSecurityClassification.IsDerivativeTitle(null).Should().BeFalse();
        InsiderSecurityClassification.IsDerivativeTitle("   ").Should().BeFalse();
    }
}
