using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins ParseEnvelope's cover-page 12(b) extraction: the three whitelisted
/// dei nonNumeric tags pair into listings by shared contextRef — the
/// per-security context a multi-listing cover dimensions on a class axis, or
/// the dimensionless context of a single-listing cover — with no context
/// resolution at all, so listings survive documents whose contexts the numeric
/// path would reject. General narrative nonNumeric text must never leak in.
/// </summary>
public class InlineXbrlParserCoverListingsTests
{
    private const string DocOpen =
        "<html "
        + "xmlns=\"http://www.w3.org/1999/xhtml\" "
        + "xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\" "
        + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:xbrldi=\"http://xbrl.org/2006/xbrldi\" "
        + "xmlns:dei=\"http://xbrl.sec.gov/dei/2023\" "
        + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2023\""
        + "><body>";

    private const string DocClose = "</body></html>";

    [Fact]
    public void ParseEnvelope_MultiListingCover_PairsTitleSymbolExchangeByContext()
    {
        // A bankrupt-retailer shape: common stock plus two listed baby bonds,
        // each 12(b) row in its own per-security context.
        var html =
            DocOpen
            + "<ix:nonNumeric name=\"dei:Security12bTitle\" contextRef=\"Common\">Common Stock, par value $0.01 per share</ix:nonNumeric>"
            + "<ix:nonNumeric name=\"dei:TradingSymbol\" contextRef=\"Common\">QVCGA</ix:nonNumeric>"
            + "<ix:nonNumeric name=\"dei:SecurityExchangeName\" contextRef=\"Common\">NASDAQ</ix:nonNumeric>"
            + "<ix:nonNumeric name=\"dei:Security12bTitle\" contextRef=\"Notes68\">6.875% Senior Secured Notes due 2068</ix:nonNumeric>"
            + "<ix:nonNumeric name=\"dei:TradingSymbol\" contextRef=\"Notes68\">QVCC</ix:nonNumeric>"
            + "<ix:nonNumeric name=\"dei:SecurityExchangeName\" contextRef=\"Notes68\">NASDAQ</ix:nonNumeric>"
            + "<ix:nonNumeric name=\"dei:Security12bTitle\" contextRef=\"Notes67\">6.375% Senior Secured Notes due 2067</ix:nonNumeric>"
            + "<ix:nonNumeric name=\"dei:TradingSymbol\" contextRef=\"Notes67\">QVCD</ix:nonNumeric>"
            + DocClose;

        var listings = new InlineXbrlParser().ParseEnvelope(html).CoverListings;

        listings.Should().HaveCount(3);
        listings[0].Title.Should().Be("Common Stock, par value $0.01 per share");
        listings[0].TradingSymbol.Should().Be("QVCGA");
        listings[0].ExchangeName.Should().Be("NASDAQ");
        listings[1].Title.Should().Be("6.875% Senior Secured Notes due 2068");
        listings[1].TradingSymbol.Should().Be("QVCC");
        listings[2].TradingSymbol.Should().Be("QVCD");
        listings[2].ExchangeName.Should().BeNull("this row's context filed no exchange fact");
    }

    [Fact]
    public void ParseEnvelope_SingleListingDimensionlessCover_YieldsOneListing()
    {
        var html =
            DocOpen
            + "<ix:nonNumeric name=\"dei:Security12bTitle\" contextRef=\"C0\">Common Stock, no par value</ix:nonNumeric>"
            + "<ix:nonNumeric name=\"dei:TradingSymbol\" contextRef=\"C0\">ACME</ix:nonNumeric>"
            + DocClose;

        var listing = new InlineXbrlParser()
            .ParseEnvelope(html)
            .CoverListings.Should()
            .ContainSingle()
            .Subject;

        listing.Title.Should().Be("Common Stock, no par value");
        listing.TradingSymbol.Should().Be("ACME");
    }

    [Fact]
    public void ParseEnvelope_SymbolWithoutTitleInContext_YieldsNoListing()
    {
        // dei:TradingSymbol also appears outside the 12(b) table (e.g. the
        // document-information block); a context with no title is not a
        // registration row.
        var html =
            DocOpen
            + "<ix:nonNumeric name=\"dei:TradingSymbol\" contextRef=\"DocInfo\">ACME</ix:nonNumeric>"
            + "<ix:nonNumeric name=\"dei:EntityRegistrantName\" contextRef=\"DocInfo\">Acme Corp</ix:nonNumeric>"
            + DocClose;

        new InlineXbrlParser().ParseEnvelope(html).CoverListings.Should().BeEmpty();
    }

    [Fact]
    public void ParseEnvelope_EscapedTitleMarkup_CollapsesToPlainText()
    {
        // escape="true" titles carry nested markup; TextContent folds it in
        // with newlines and runs of spaces that must collapse to single spaces.
        var html =
            DocOpen
            + "<ix:nonNumeric name=\"dei:Security12bTitle\" contextRef=\"C1\" escape=\"true\">Depositary Shares, each representing\n      a 1/1,000th interest in a share of <span>Series A Preferred Stock</span></ix:nonNumeric>"
            + "<ix:nonNumeric name=\"dei:TradingSymbol\" contextRef=\"C1\">ACME-PA</ix:nonNumeric>"
            + DocClose;

        var listing = new InlineXbrlParser()
            .ParseEnvelope(html)
            .CoverListings.Should()
            .ContainSingle()
            .Subject;

        listing
            .Title.Should()
            .Be(
                "Depositary Shares, each representing a 1/1,000th interest in a share of Series A Preferred Stock"
            );
    }

    [Fact]
    public void ParseEnvelope_RepeatedFactInSameContext_KeepsFirstNonEmptyValue()
    {
        var html =
            DocOpen
            + "<ix:nonNumeric name=\"dei:Security12bTitle\" contextRef=\"C1\">Common Stock</ix:nonNumeric>"
            + "<ix:nonNumeric name=\"dei:Security12bTitle\" contextRef=\"C1\">Common Stock (restated rendering)</ix:nonNumeric>"
            + "<ix:nonNumeric name=\"dei:TradingSymbol\" contextRef=\"C1\">ACME</ix:nonNumeric>"
            + DocClose;

        var listing = new InlineXbrlParser()
            .ParseEnvelope(html)
            .CoverListings.Should()
            .ContainSingle()
            .Subject;

        listing.Title.Should().Be("Common Stock");
    }

    [Fact]
    public void Parse_UnchangedByCoverExtraction_StillReturnsNumericFacts()
    {
        // The numeric contract is untouched: Parse still returns the facts a
        // combined envelope carries, and the cover listings ride the same pass.
        var html =
            DocOpen
            + "<div style=\"display:none\"><ix:header><ix:resources>"
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2026-03-31</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"usd\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + "</ix:resources></ix:header></div>"
            + "<ix:nonNumeric name=\"dei:Security12bTitle\" contextRef=\"C1\">Common Stock</ix:nonNumeric>"
            + "<ix:nonNumeric name=\"dei:TradingSymbol\" contextRef=\"C1\">ACME</ix:nonNumeric>"
            + "<ix:nonFraction name=\"us-gaap:Assets\" contextRef=\"C1\" unitRef=\"usd\" decimals=\"0\">1000</ix:nonFraction>"
            + DocClose;

        var result = new InlineXbrlParser().ParseEnvelope(html);

        result.Facts.Should().ContainSingle().Which.Value.Should().Be(1000m);
        result.CoverListings.Should().ContainSingle().Which.TradingSymbol.Should().Be("ACME");
    }
}
