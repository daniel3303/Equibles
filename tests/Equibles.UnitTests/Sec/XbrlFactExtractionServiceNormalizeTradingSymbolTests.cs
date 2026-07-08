using Equibles.Sec.FinancialFacts.HostedService.Services;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins trading-symbol normalization: filings write class tickers with dots
/// ("BRK.B") where the platform's ticker feed uses dashes ("BRK-B"), so both
/// must normalize to the same key or a class share never matches its own 12(b)
/// row. Placeholder values filers type for unlisted securities must resolve to
/// null BEFORE separator stripping — "N/A" stripped first would collide with
/// the genuine ticker "NA".
/// </summary>
public class XbrlFactExtractionServiceNormalizeTradingSymbolTests
{
    [Theory]
    [InlineData("BRK.B", "BRKB")]
    [InlineData("BRK-B", "BRKB")]
    [InlineData(" qvcc ", "QVCC")]
    [InlineData("RDS/A", "RDSA")]
    [InlineData("NA", "NA")]
    public void NormalizeTradingSymbol_Value_NormalizesToMatchKey(string raw, string expected)
    {
        XbrlFactExtractionService.NormalizeTradingSymbol(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("N/A")]
    [InlineData("n/a")]
    [InlineData("None")]
    [InlineData("Not Applicable")]
    [InlineData("THIS-SYMBOL-IS-FAR-TOO-LONG-TO-BE-A-REAL-TICKER")]
    public void NormalizeTradingSymbol_PlaceholderOrUnusable_ReturnsNull(string raw)
    {
        XbrlFactExtractionService.NormalizeTradingSymbol(raw).Should().BeNull();
    }
}
