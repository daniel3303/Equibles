using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

public class IrPlatformClassifierTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_NoHtml_ReturnsUnknown(string html)
    {
        // Contract: nothing to inspect → not classified (distinct from a real page
        // that simply matches no vendor).
        IrPlatformClassifier.Classify(html).Should().Be(IrPlatformType.Unknown);
    }

    [Theory]
    [InlineData(
        "<script src=\"https://s1.q4cdn.com/123/files/app.js\"></script>",
        IrPlatformType.Q4Inc
    )]
    [InlineData("<link href=\"https://www.q4inc.com/styles.css\">", IrPlatformType.Q4Inc)]
    [InlineData(
        "<iframe src=\"https://cts.businesswire.com/feed\"></iframe>",
        IrPlatformType.BusinessWire
    )]
    [InlineData(
        "<script src=\"https://www.globenewswire.com/rss\"></script>",
        IrPlatformType.Notified
    )]
    [InlineData("<a href=\"https://app.notified.com/login\">IR</a>", IrPlatformType.Notified)]
    [InlineData(
        "<script src=\"https://ir.nasdaq.com/feed.js\"></script>",
        IrPlatformType.NasdaqIrInsight
    )]
    public void Classify_VendorMarkerPresent_ReturnsThatPlatform(
        string html,
        IrPlatformType expected
    )
    {
        IrPlatformClassifier.Classify(html).Should().Be(expected);
    }

    [Fact]
    public void Classify_MarkerMatchIsCaseInsensitive()
    {
        IrPlatformClassifier
            .Classify("<SCRIPT SRC=\"HTTPS://S1.Q4CDN.COM/A.JS\"></SCRIPT>")
            .Should()
            .Be(IrPlatformType.Q4Inc);
    }

    [Fact]
    public void Classify_RealPageNoVendorMarker_ReturnsCustom()
    {
        // An IR page on a bespoke stack matches no vendor CDN — that's Custom, not
        // Unknown (we did inspect a real page).
        const string html =
            "<html><head><title>Investor Relations - Acme</title></head>"
            + "<body><script src=\"/assets/app.js\"></script></body></html>";

        IrPlatformClassifier.Classify(html).Should().Be(IrPlatformType.Custom);
    }
}
