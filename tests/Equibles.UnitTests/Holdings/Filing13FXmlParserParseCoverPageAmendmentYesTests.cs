using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Sibling to the IsAmendment-handling tests under HoldingsImportService
/// (TryResolveAmendmentTargetLowercaseY). Those pin the "Y"/"y" path. The
/// upstream gate inside `Filing13FXmlParser.ParseCoverPage` is the
/// `IsAmendmentValue` helper, which separately tolerates the textual
/// "yes" variant (some pre-XBRL 13F submissions ship the cover-page flag
/// that way). The "yes" branch has no end-to-end pin, so a regression that
/// drops the `yes` arm in `IsAmendmentValue` would silently misclassify
/// every such amendment as a fresh filing — the reconciliation logic
/// downstream never fires and the original holdings remain stale.
/// </summary>
public class Filing13FXmlParserParseCoverPageAmendmentYesTests
{
    [Fact]
    public void ParseCoverPage_IsAmendmentTextualYes_FlagsAmendmentTrue()
    {
        // A canonical primary_doc.xml with the cover-page IsAmendment field
        // set to the textual "yes" variant. The parser must return
        // IsAmendment = true so the downstream reconciliation pipeline
        // treats this as an amendment, matching the contract pinned for
        // the "Y" / "y" variants elsewhere.
        var xml =
            "<?xml version=\"1.0\"?>"
            + "<edgarSubmission xmlns=\"http://www.sec.gov/edgar/thirteenffiler\">"
            + "  <headerData><cik>1067983</cik></headerData>"
            + "  <coverPage>"
            + "    <reportCalendarOrQuarter>09-30-2024</reportCalendarOrQuarter>"
            + "    <isAmendment>yes</isAmendment>"
            + "    <filingManager><name>Test Filer</name></filingManager>"
            + "  </coverPage>"
            + "</edgarSubmission>";
        var parser = new Filing13FXmlParser();

        var filing = parser.ParseCoverPage(
            xml,
            "0001000000-24-000001",
            "1067983",
            new DateOnly(2024, 11, 14)
        );

        filing
            .IsAmendment.Should()
            .BeTrue(
                "IsAmendmentValue tolerates the textual 'yes' variant in addition to 'Y'/'true'/'1' so older 13F submissions are classified consistently"
            );
    }
}
