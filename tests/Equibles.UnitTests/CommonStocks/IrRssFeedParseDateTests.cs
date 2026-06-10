using System.Globalization;
using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Pins IrRssFeed.ParseDate's contract: RSS pubDate values are RFC 2822 with
/// English month/day names and a numeric offset, so they must parse and
/// normalise to UTC regardless of the host culture — a CurrentCulture-bound
/// parse would return null on any non-English server locale.
/// </summary>
public class IrRssFeedParseDateTests
{
    [Fact]
    public void ParseDate_Rfc2822WithOffsetUnderGermanCulture_NormalisesToUtc()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var result = IrRssFeed.ParseDate("Tue, 10 Jun 2025 14:30:00 -0500");

            result.Should().Be(new DateTime(2025, 6, 10, 19, 30, 0, DateTimeKind.Utc));
            result!.Value.Kind.Should().Be(DateTimeKind.Utc, "the contract promises UTC");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
