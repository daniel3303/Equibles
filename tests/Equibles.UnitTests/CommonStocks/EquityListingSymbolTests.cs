using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Contract: a US listing is written as its bare ticker, a venue listing as MIC:TICKER
/// (MIC-TICKER where a file name needs it), and a venue listing missing its MIC falls back
/// to the bare ticker rather than composing a separator onto nothing.
/// </summary>
public class EquityListingSymbolTests
{
    [Theory]
    [InlineData("AIR", "US", null, "AIR", "AIR")]
    [InlineData("AIR", "US", "XNYS", "AIR", "AIR")]
    [InlineData("AIR", "FR", "XPAR", "XPAR:AIR", "XPAR-AIR")]
    [InlineData("BRK.B", "US", null, "BRK.B", "BRK.B")]
    [InlineData("SHEL", "GB", "XLON", "XLON:SHEL", "XLON-SHEL")]
    [InlineData("AIR", "FR", null, "AIR", "AIR")]
    [InlineData("AIR", "FR", " ", "AIR", "AIR")]
    [InlineData(null, "FR", "XPAR", null, null)]
    [InlineData(" ", "US", null, null, null)]
    public void Display_And_FileSafe(
        string ticker,
        string country,
        string mic,
        string display,
        string fileSafe
    )
    {
        EquityListingSymbol.Display(ticker, country, mic).Should().Be(display);
        EquityListingSymbol.FileSafe(ticker, country, mic).Should().Be(fileSafe);
    }

    [Fact]
    public void IssuerOverloads_ReadThePresentationListing()
    {
        var paris = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AIR",
            MarketCountryCode: "FR",
            MarketIdentifierCode: "XPAR",
            IdentityState: EquityIdentityState.Verified,
            TradingCurrency: "EUR",
            QuoteUnitMultiplier: 1m
        );
        var nyse = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "AIR");
        var noPresentation = new EquityIssuer();

        EquityListingSymbol.Display(paris).Should().Be("XPAR:AIR");
        EquityListingSymbol.FileSafe(paris).Should().Be("XPAR-AIR");
        EquityListingSymbol.Display(nyse).Should().Be("AIR");
        EquityListingSymbol.Display(noPresentation).Should().BeNull();
        EquityListingSymbol.Display((EquityIssuer)null).Should().BeNull();
        EquityListingSymbol.Display((EquityListing)null).Should().BeNull();
    }

    [Fact]
    public void DisplayForm_CanNeverCollideWithAListedTicker()
    {
        // A directory ticker may hold only ASCII letters, digits, dots and dashes, so the colon
        // in a venue-qualified symbol is proof it is not a ticker.
        TickerNormalizer.NormalizeListed("XPAR:AIR").Should().BeNull();
    }

    [Theory]
    [InlineData("XPAR:AIR", "XPAR", "AIR")]
    [InlineData(" xlon:bt-a ", "XLON", "BT-A")]
    [InlineData("XHKG:00005", "XHKG", "00005")]
    public void TryParse_ReadsTheDisplaySpellingBack(string symbol, string mic, string ticker)
    {
        EquityListingSymbol
            .TryParse(symbol, out var parsedMic, out var parsedTicker)
            .Should()
            .BeTrue();
        parsedMic.Should().Be(mic);
        parsedTicker.Should().Be(ticker);
    }

    [Theory]
    [InlineData("AIR")]
    [InlineData("BRK.B")]
    [InlineData("XPA:AIR")]
    [InlineData("XPARI:AIR")]
    [InlineData("XPAR:")]
    [InlineData("XP-R:AIR")]
    [InlineData("XPAR:AI R")]
    [InlineData(":AIR")]
    [InlineData(null)]
    public void TryParse_RejectsAnythingButMicColonTicker(string symbol)
    {
        EquityListingSymbol.TryParse(symbol, out var mic, out var ticker).Should().BeFalse();
        mic.Should().BeNull();
        ticker.Should().BeNull();
    }

    // XPAR:AIR is Airbus and AIR is AAR Corp, so the notice must not send a caller to the bare ticker.
    [Fact]
    public void UsOnlyNotice_NamesTheSymbol_AndNeverSuggestsItsBareTicker()
    {
        var notice = EquityListingSymbol.UsOnlyNotice(" XPAR:AIR ");

        notice.Should().StartWith("'XPAR:AIR' names a listing outside the US");
        notice.Should().Contain("different US company").And.NotContain("instead");
    }
}
