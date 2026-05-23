using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// Lane B (coverage): exercises the full ParseCoverPage path — lines 29-71
// are zero-hit today. A realistic SEC primary_doc.xml drives through header
// parsing, cover-page field extraction, address, and the otherManager2 loop.
public class Filing13FXmlParserParseCoverPageTests
{
    private readonly Filing13FXmlParser _sut = new();

    [Fact]
    public void ParseCoverPage_FullCoverPage_ExtractsAllFieldsIncludingOtherManagers()
    {
        var xml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<edgarSubmission xmlns=\"http://www.sec.gov/edgar/thirteenffiler\">"
            + "  <headerData>"
            + "    <cik>0001067983</cik>"
            + "  </headerData>"
            + "  <coverPage>"
            + "    <reportCalendarOrQuarter>03-31-2024</reportCalendarOrQuarter>"
            + "    <isAmendment>false</isAmendment>"
            + "    <filingManager>"
            + "      <name>BERKSHIRE HATHAWAY INC</name>"
            + "      <address>"
            + "        <city>OMAHA</city>"
            + "        <stateOrCountry>NE</stateOrCountry>"
            + "      </address>"
            + "    </filingManager>"
            + "    <form13FFileNumber>028-00338</form13FFileNumber>"
            + "    <crdNumber>314159</crdNumber>"
            + "    <otherManager2>"
            + "      <sequenceNumber>1</sequenceNumber>"
            + "      <otherManager><name>General Re-New England Asset Mgmt</name></otherManager>"
            + "    </otherManager2>"
            + "    <otherManager2>"
            + "      <sequenceNumber>3</sequenceNumber>"
            + "      <otherManager><name>New England Asset Management</name></otherManager>"
            + "    </otherManager2>"
            + "  </coverPage>"
            + "</edgarSubmission>";

        var result = _sut.ParseCoverPage(
            xml,
            accessionNumber: "0000950123-24-007578",
            cik: "0001067983",
            filingDate: new DateOnly(2024, 5, 15)
        );

        result.AccessionNumber.Should().Be("0000950123-24-007578");
        result.Cik.Should().Be("1067983");
        result.FilingDate.Should().Be(new DateOnly(2024, 5, 15));
        result.PeriodOfReport.Should().Be(new DateOnly(2024, 3, 31));
        result.IsAmendment.Should().BeFalse();
        result.FilingManagerName.Should().Be("BERKSHIRE HATHAWAY INC");
        result.City.Should().Be("OMAHA");
        result.StateOrCountry.Should().Be("NE");
        result.Form13FFileNumber.Should().Be("028-00338");
        result.CrdNumber.Should().Be("314159");
        result.OtherManagers.Should().HaveCount(2);
        result.OtherManagers[1].Should().Be("General Re-New England Asset Mgmt");
        result.OtherManagers[3].Should().Be("New England Asset Management");
    }
}
