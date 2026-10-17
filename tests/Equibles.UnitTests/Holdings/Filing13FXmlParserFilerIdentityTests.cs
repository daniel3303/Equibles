using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// The filer's own identity numbers are direct children of coverPage. The cover's otherManagersInfo
/// list nests the same element names for the managers that report FOR this filer, so a descendant
/// scan hands a parent's numbers to a filer that leaves its own blank.
/// </summary>
public class Filing13FXmlParserFilerIdentityTests
{
    private readonly Filing13FXmlParser _sut = new();

    [Fact]
    public void OwnNumbers_AreReadIncludingTheSecFileNumber()
    {
        var result = Parse(
            "<form13FFileNumber>028-16741</form13FFileNumber>"
                + "<crdNumber>000171004</crdNumber>"
                + "<secFileNumber>801-80534</secFileNumber>"
        );

        result.Form13FFileNumber.Should().Be("028-16741");
        result.CrdNumber.Should().Be("000171004");
        result.SecFileNumber.Should().Be("801-80534");
    }

    [Fact]
    public void BlankOwnNumbers_WithAParentListed_DoNotBorrowTheParentsNumbers()
    {
        var result = Parse(
            "<otherManagersInfo><otherManager>"
                + "<cik>0002026079</cik>"
                + "<form13FFileNumber>028-24289</form13FFileNumber>"
                + "<crdNumber>000000361</crdNumber>"
                + "<secFileNumber>801-11111</secFileNumber>"
                + "<name>PARENT REPORTING FOR US</name>"
                + "</otherManager></otherManagersInfo>"
        );

        result.Form13FFileNumber.Should().BeNull();
        result.CrdNumber.Should().BeNull();
        result.SecFileNumber.Should().BeNull();
        result
            .CoverPageOtherManagers.Should()
            .ContainSingle()
            .Which.CrdNumber.Should()
            .Be("000000361");
    }

    [Fact]
    public void PresentButEmptyNumbers_AreAbsent()
    {
        var result = Parse(
            "<form13FFileNumber> </form13FFileNumber><crdNumber></crdNumber><secFileNumber/>"
        );

        result.Form13FFileNumber.Should().BeNull();
        result.CrdNumber.Should().BeNull();
        result.SecFileNumber.Should().BeNull();
    }

    private Parsed13FFiling Parse(string coverPageIdentity)
    {
        var xml =
            "<edgarSubmission>"
            + "  <headerData><cik>0000895421</cik></headerData>"
            + "  <coverPage>"
            + "    <reportCalendarOrQuarter>03-31-2026</reportCalendarOrQuarter>"
            + "    <filingManager><name>FILER</name></filingManager>"
            + "    <reportType>13F COMBINATION REPORT</reportType>"
            + coverPageIdentity
            + "  </coverPage>"
            + "</edgarSubmission>";
        return _sut.ParseCoverPage(
            xml,
            accessionNumber: "0000895421-26-000183",
            cik: "0000895421",
            filingDate: new DateOnly(2026, 5, 15)
        );
    }
}
