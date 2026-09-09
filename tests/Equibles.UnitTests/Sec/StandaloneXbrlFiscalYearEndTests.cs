using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class StandaloneXbrlFiscalYearEndTests
{
    private static string Envelope(
        string metadata = "--12-31",
        string qualifiers = "",
        string attribute = ""
    ) =>
        $$"""
            <xbrl xmlns="http://www.xbrl.org/2003/instance" xmlns:dei="http://xbrl.sec.gov/dei/2020" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            <context id="q"><entity><identifier scheme="http://www.sec.gov/CIK">0001777393</identifier>{{qualifiers}}</entity>
            <period><startDate>2020-01-01</startDate><endDate>2020-12-31</endDate></period></context>
            <dei:CurrentFiscalYearEndDate contextRef="q" {{attribute}}>{{metadata}}</dei:CurrentFiscalYearEndDate>
            </xbrl>
            """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsOnlySourceStatedConsolidatedCalendar(bool wrapper)
    {
        var xml = Envelope();
        if (wrapper)
            xml = "<XBRL>\n<?xml version=\"1.0\"?>\n" + xml + "\n</XBRL>";
        var actual = new StandaloneXbrlParser()
            .ParseFiscalYearEnds(xml)
            .Should()
            .ContainSingle()
            .Which;
        actual.Cik.Should().Be("0001777393");
        actual.PeriodStart.Should().Be(new DateOnly(2020, 1, 1));
        actual.PeriodEnd.Should().Be(new DateOnly(2020, 12, 31));
        actual.Month.Should().Be(12);
        actual.Day.Should().Be(31);
    }

    [Theory]
    [InlineData("--02-30", "", "")]
    [InlineData("--12-31", "<segment/>", "")]
    [InlineData("--12-31", "", "xsi:nil=\"true\"")]
    [InlineData("--12-31", "", "xsi:nil=\"1\"")]
    public void RefusesUnusableMetadata(string value, string qualifiers, string attribute)
    {
        new StandaloneXbrlParser()
            .ParseFiscalYearEnds(Envelope(value, qualifiers, attribute))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void RefusesForeignNamespacesAndAmbiguousContextIds()
    {
        var parser = new StandaloneXbrlParser();
        parser
            .ParseFiscalYearEnds(
                Envelope().Replace("http://xbrl.sec.gov/dei/2020", "https://example.com/dei/2020")
            )
            .Should()
            .BeEmpty();
        parser
            .ParseFiscalYearEnds(
                Envelope()
                    .Replace(
                        "</context>",
                        "</context><context id=\"q\"><entity><identifier scheme=\"http://www.sec.gov/CIK\">9999</identifier></entity><period><instant>2020-12-31</instant></period></context>"
                    )
            )
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void RefusesDtdAndMalformedXml()
    {
        var parser = new StandaloneXbrlParser();
        parser
            .ParseFiscalYearEnds("<!DOCTYPE xbrl [<!ENTITY date '--12-31'>]>" + Envelope("&date;"))
            .Should()
            .BeEmpty();
        parser.ParseFiscalYearEnds(Envelope().Replace("</xbrl>", "")).Should().BeEmpty();
    }
}
