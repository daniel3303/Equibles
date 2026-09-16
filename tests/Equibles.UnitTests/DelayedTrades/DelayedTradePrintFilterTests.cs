using Equibles.DelayedTrades.BusinessLogic.Prints;
using Equibles.Integrations.DelayedTrades;

namespace Equibles.UnitTests.DelayedTrades;

/// <summary>
/// Contract: a counted print is a priced, sized, monetary trade; a price-forming print is a counted lit
/// print (mechanism 1 or 5) with no benchmark, negotiation or non-contributing flag, so the closing
/// auction counts and a dark, off-book or benchmark print only adds volume.
/// </summary>
public class DelayedTradePrintFilterTests
{
    private static readonly DateTime Traded = new(2026, 9, 15, 15, 35, 18, DateTimeKind.Utc);

    internal static DelayedTradePrint Print(
        decimal price = 4.65m,
        decimal quantity = 100m,
        string notation = "MONE",
        string currency = "EUR",
        string mechanism = "1",
        string negotiation = "-",
        string benchmark = "-",
        string contribution = "-",
        bool missingPrice = false,
        DelayedTradeModification modification = DelayedTradeModification.None,
        string tradeId = "T1",
        DateTime? tradedAt = null,
        DateTime? publishedAt = null,
        string isin = "PTEDP0AM0009",
        string venue = "XLIS",
        int line = 3
    ) =>
        new(
            isin,
            venue,
            tradedAt ?? Traded,
            publishedAt ?? tradedAt ?? Traded,
            price,
            quantity,
            currency,
            notation,
            modification,
            tradeId,
            mechanism,
            benchmark,
            contribution,
            negotiation,
            missingPrice,
            line
        );

    [Fact]
    public void AClosingAuctionPrint_IsPriceForming()
    {
        DelayedTradePrintFilter.IsPriceForming(Print(mechanism: "1")).Should().BeTrue();
        DelayedTradePrintFilter.IsPriceForming(Print(mechanism: "5")).Should().BeTrue();
        DelayedTradePrintFilter.IsPriceForming(Print(contribution: "P")).Should().BeTrue();
    }

    [Theory]
    [InlineData("3", "-", "RFPT", "-", true)]
    [InlineData("4", "NLIQ", "-", "-", false)]
    [InlineData("4", "-", "BENC", "-", false)]
    [InlineData("4", "-", "-", "NPFT", false)]
    [InlineData("1", "-", "-", "NPFT", false)]
    [InlineData("1", "OILQ", "-", "-", false)]
    public void AFlaggedPrint_CountsVolumeButNeverPrice(
        string mechanism,
        string negotiation,
        string benchmark,
        string contribution,
        bool dark
    )
    {
        var print = Print(
            mechanism: mechanism,
            negotiation: negotiation,
            benchmark: benchmark,
            contribution: contribution
        );
        DelayedTradePrintFilter.IsCounted(print).Should().BeTrue();
        DelayedTradePrintFilter.IsPriceForming(print).Should().BeFalse();
        DelayedTradePrintFilter.IsDark(print).Should().Be(dark);
    }

    [Fact]
    public void AnUnpricedUnsizedOrNonMonetaryPrint_IsNotCounted()
    {
        DelayedTradePrintFilter.IsCounted(Print(missingPrice: true)).Should().BeFalse();
        DelayedTradePrintFilter.IsCounted(Print(price: 0m)).Should().BeFalse();
        DelayedTradePrintFilter.IsCounted(Print(quantity: 0m)).Should().BeFalse();
        DelayedTradePrintFilter.IsCounted(Print(notation: "PERC")).Should().BeFalse();
        DelayedTradePrintFilter.IsCounted(null).Should().BeFalse();
    }

    [Fact]
    public void IsDelayed_DropsAPrintPublishedInsideTheDelay()
    {
        var fetched = new DateTime(2026, 9, 15, 16, 0, 0, DateTimeKind.Utc);
        var delay = TimeSpan.FromMinutes(15);
        DelayedTradePrintFilter
            .IsDelayed(Print(publishedAt: fetched.AddMinutes(-15)), fetched, delay)
            .Should()
            .BeTrue();
        DelayedTradePrintFilter
            .IsDelayed(Print(publishedAt: fetched.AddMinutes(-14)), fetched, delay)
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData("EUR", "EUR", 1, true)]
    [InlineData("GBX", "GBP", 0.01, true)]
    [InlineData("GBp", "GBP", 0.01, true)]
    [InlineData("GBP", "GBP", 0.01, false)]
    [InlineData("USD", "EUR", 1, false)]
    public void MatchesQuotation_ResolvesTheVenueTokenAgainstTheStoredUnit(
        string token,
        string currency,
        double multiplier,
        bool expected
    )
    {
        DelayedTradePrintFilter
            .MatchesQuotation(Print(currency: token), currency, (decimal)multiplier)
            .Should()
            .Be(expected);
    }
}
