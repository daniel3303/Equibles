using Equibles.Sec.BusinessLogic;
using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Sec;

// Registration statements, tender offers and merger proxies attach an inline-XBRL "EX-FILING FEES"
// exhibit (the shape of Drilling Tools' S-4, 0001213900-26-108565). It is fee metadata, never the filing,
// so it must neither become the captured inline original nor join the primary's document set.
public class SecDocumentEnvelopeParserFilingFeeExhibitTests
{
    private const string FeeTable =
        "<html xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\"><body>"
        + "<ix:nonFraction name=\"ffd:FeeAmt\" contextRef=\"c-1\" unitRef=\"USD\">1,234</ix:nonFraction>"
        + "</body></html>";

    private const string InlinePrimary =
        "<html xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\"><body><p>Proxy statement/prospectus</p>"
        + "<ix:nonNumeric name=\"dei:EntityRegistrantName\" contextRef=\"c-1\">Acme</ix:nonNumeric>"
        + "</body></html>";

    private const string PlainPrimary =
        "<html><body><p>Proxy statement/prospectus</p></body></html>";

    private static string Submission(
        params (string Type, string FileName, string Body)[] documents
    ) =>
        "<SEC-DOCUMENT>\n"
        + string.Concat(
            documents.Select(
                (document, index) =>
                    $"<DOCUMENT>\n<TYPE>{document.Type}\n<SEQUENCE>{index + 1}\n<FILENAME>{document.FileName}\n<TEXT>\n{document.Body}\n</TEXT>\n</DOCUMENT>\n"
            )
        )
        + "</SEC-DOCUMENT>";

    [Fact]
    public void TryExtractXbrlEnvelope_OnlyTheFeeExhibitIsInline_CapturesNoOriginal()
    {
        var envelope = Submission(
            ("S-4", "acme-s4.htm", PlainPrimary),
            ("EX-FILING FEES", "acme-ex-fee.htm", FeeTable)
        );

        var found = SecDocumentEnvelopeParser.TryExtractXbrlEnvelope(
            envelope,
            "acme-s4.htm",
            out _,
            out _,
            out _
        );

        found.Should().BeFalse();
    }

    [Fact]
    public void TryExtractXbrlEnvelope_InlinePrimaryWithFeeExhibit_CapturesThePrimaryAlone()
    {
        var envelope = Submission(
            ("S-4", "acme-s4.htm", InlinePrimary),
            ("EX-FILING FEES", "acme-ex-fee.htm", FeeTable)
        );

        SecDocumentEnvelopeParser
            .TryExtractXbrlEnvelope(
                envelope,
                "acme-s4.htm",
                out var type,
                out var sourceFileName,
                out var content
            )
            .Should()
            .BeTrue();

        type.Should().Be(XbrlType.InlineIxbrl);
        sourceFileName.Should().Be("acme-s4.htm");
        content.Should().NotContain("ffd:FeeAmt");
    }
}
