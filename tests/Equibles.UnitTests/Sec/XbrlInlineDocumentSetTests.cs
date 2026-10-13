using Equibles.Sec.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// A 40-F tags only its cover in the primary document and its statements in an exhibit of the
/// same inline document set (GIB 2025: one cover fact, 2,665 statement facts in EX-99.2). The
/// capture must keep every inline document, primary first, so one parse reads both.
/// </summary>
public class XbrlInlineDocumentSetTests
{
    private const string Cover =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><html "
        + "xmlns=\"http://www.w3.org/1999/xhtml\" "
        + "xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\" "
        + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:dei=\"http://xbrl.sec.gov/dei/2024\""
        + "><head><title>40-F</title></head><body><div style=\"display:none\"><ix:header><ix:resources>"
        + "<xbrli:context id=\"c-1\"><xbrli:entity><xbrli:identifier scheme=\"http://www.sec.gov/CIK\">0001061574</xbrli:identifier></xbrli:entity>"
        + "<xbrli:period><xbrli:startDate>2024-10-01</xbrli:startDate><xbrli:endDate>2025-09-30</xbrli:endDate></xbrli:period></xbrli:context>"
        + "</ix:resources></ix:header></div>"
        + "<ix:nonNumeric name=\"dei:Security12bTitle\" contextRef=\"c-1\">Class A subordinate voting shares</ix:nonNumeric>"
        + "<ix:nonNumeric name=\"dei:TradingSymbol\" contextRef=\"c-1\">GIB</ix:nonNumeric>"
        + "</body></html>";

    // The statements' taxonomy prefix is declared only on the exhibit's own root.
    private const string Statements =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><html "
        + "xmlns=\"http://www.w3.org/1999/xhtml\" "
        + "xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\" "
        + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:iso4217=\"http://www.xbrl.org/2003/iso4217\" "
        + "xmlns:ifrs-full=\"https://xbrl.ifrs.org/taxonomy/2024-03-27/ifrs-full\""
        + "><head><title>EX-99.2</title></head><body>"
        + "<xbrli:unit id=\"CAD\"><xbrli:measure>iso4217:CAD</xbrli:measure></xbrli:unit>"
        + "<ix:nonFraction name=\"ifrs-full:Revenue\" contextRef=\"c-1\" unitRef=\"CAD\" decimals=\"-3\" scale=\"3\">15,904,108</ix:nonFraction>"
        + "</body></html>";

    private static string Submission(params (string Type, string FileName, string Body)[] documents) =>
        "<SEC-DOCUMENT>\n"
        + string.Concat(
            documents.Select(
                (document, index) =>
                    $"<DOCUMENT>\n<TYPE>{document.Type}\n<SEQUENCE>{index + 1}\n<FILENAME>{document.FileName}\n<TEXT>\n{document.Body}\n</TEXT>\n</DOCUMENT>\n"
            )
        )
        + "</SEC-DOCUMENT>";

    private static string Capture(string envelope, string primary, out string sourceFileName)
    {
        SecDocumentEnvelopeParser
            .TryExtractXbrlEnvelope(envelope, primary, out var type, out sourceFileName, out var content)
            .Should()
            .BeTrue();
        type.Should().Be(XbrlType.InlineIxbrl);
        return content;
    }

    [Fact]
    public void TryExtractXbrlEnvelope_CoverAndStatementExhibit_CapturesBothPrimaryFirst()
    {
        var envelope = Submission(
            ("EX-99.2", "gib-ex992.htm", Statements),
            ("40-F", "gib-40f.htm", Cover),
            ("EX-99.1", "gib-ex991.htm", "<html><body>Annual information form</body></html>"),
            ("XML", "gib-40f_htm.xml", "<xbrl>extracted instance</xbrl>")
        );

        var content = Capture(envelope, "gib-40f.htm", out var sourceFileName);

        sourceFileName.Should().Be("gib-40f.htm");
        content.Should().Be(Cover + "\n" + Statements);
    }

    [Fact]
    public void TryExtractXbrlEnvelope_UnmatchedPrimaryName_CapturesTheSetUnderItsFirstDocument()
    {
        var envelope = Submission(("40-F", "gib-40f.htm", Cover), ("EX-99.2", "gib-ex992.htm", Statements));

        var content = Capture(envelope, "renamed.htm", out var sourceFileName);

        sourceFileName.Should().Be("gib-40f.htm");
        content.Should().Be(Cover + "\n" + Statements);
    }

    [Fact]
    public void ParseEnvelope_CapturedDocumentSet_ReadsExhibitFactsAndCoverListings()
    {
        var envelope = Submission(("40-F", "gib-40f.htm", Cover), ("EX-99.2", "gib-ex992.htm", Statements));

        var result = new InlineXbrlParser().ParseEnvelope(Capture(envelope, "gib-40f.htm", out _));

        var revenue = result.Facts.Should().ContainSingle().Subject;
        revenue.Tag.Should().Be("Revenue");
        revenue.Unit.Should().Be("CAD");
        revenue.Value.Should().Be(15_904_108_000m);
        revenue.PeriodEnd.Should().Be(new DateOnly(2025, 9, 30));
        result.CoverListings.Should().ContainSingle().Which.TradingSymbol.Should().Be("GIB");
    }

    // An empty <title/> swallows the rest of a page under HTML rules; once two roots are joined
    // the parser can no longer expand it, so each document is expanded before the join.
    [Fact]
    public void ParseEnvelope_DocumentSetWithEmptyTitles_ReadsExhibitFactsAndCoverListings()
    {
        var envelope = Submission(
            ("40-F", "gib-40f.htm", Cover.Replace("<title>40-F</title>", "<title/>")),
            ("EX-99.2", "gib-ex992.htm", Statements.Replace("<title>EX-99.2</title>", "<title/>"))
        );

        var result = new InlineXbrlParser().ParseEnvelope(Capture(envelope, "gib-40f.htm", out _));

        result.Facts.Should().ContainSingle().Which.Unit.Should().Be("CAD");
        result.CoverListings.Should().ContainSingle().Which.TradingSymbol.Should().Be("GIB");
    }

    // A uuencoded image can spell "<IX:" by chance; it is never part of the document set.
    [Fact]
    public void TryExtractXbrlEnvelope_BinaryBlockSpellingAnInlineMarker_IsNotPartOfTheSet()
    {
        var envelope = Submission(
            ("40-F", "gib-40f.htm", Cover),
            ("GRAPHIC", "gib-logo.jpg", "begin 644 gib-logo.jpg\nM<IX:Q3#2\nend")
        );

        Capture(envelope, "gib-40f.htm", out _).Should().Be(Cover);
    }

    [Fact]
    public void TryExtractXbrlEnvelope_NoHtmlInlineDocument_KeepsTheFirstInlineBlock()
    {
        var envelope = Submission(("40-F", "gib-40f.txt", Cover), ("EX-99.2", "gib-ex992.txt", Statements));

        Capture(envelope, "renamed.htm", out var sourceFileName).Should().Be(Cover);
        sourceFileName.Should().Be("gib-40f.txt");
    }

    [Fact]
    public void JoinDocumentSet_OverTheParseCeiling_KeepsThePrimary()
    {
        SecDocumentEnvelopeParser.JoinDocumentSet([Statements, Cover], 1, maxBytes: 100).Should().Be(Cover);
        SecDocumentEnvelopeParser
            .JoinDocumentSet([Statements, Cover], 1, maxBytes: 10_000)
            .Should()
            .Be(Cover + "\n" + Statements);
    }

    [Fact]
    public void MaxInlineDocumentSetBytes_MatchesTheFactParserCeiling()
    {
        SecDocumentEnvelopeParser
            .MaxInlineDocumentSetBytes.Should()
            .Be(XbrlFactExtractionService.MaxParseableEnvelopeBytes);
    }
}
