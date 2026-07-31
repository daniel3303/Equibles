using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Filing13FXmlParserOtherManagerIdentityTests
{
    [Fact]
    public void ParseCoverPage_OtherManager2WithIdentifiers_KeepsAllOfThem()
    {
        // The identifiers are the only thing that can tie a co-manager to an institution: names
        // are not safe to match on, so a parser that reads <name> and drops the siblings leaves
        // every subsidiary in a combination report permanently unlinkable. The CIK is stored the
        // way filer CIKs are stored — leading zeros gone — or the join silently never matches.
        var xml =
            "<edgarSubmission>"
            + "  <headerData><cik>0000886982</cik></headerData>"
            + "  <coverPage><reportCalendarOrQuarter>03-31-2026</reportCalendarOrQuarter></coverPage>"
            + "  <summaryPage>"
            + "    <otherManagers2Info>"
            + "      <otherManager2>"
            + "        <sequenceNumber>1</sequenceNumber>"
            + "        <otherManager>"
            + "          <cik>0000769993</cik>"
            + "          <form13FFileNumber>028-00687</form13FFileNumber>"
            + "          <crdNumber>000000361</crdNumber>"
            + "          <name>GOLDMAN SACHS &amp; CO. LLC</name>"
            + "        </otherManager>"
            + "      </otherManager2>"
            + "    </otherManagers2Info>"
            + "  </summaryPage>"
            + "</edgarSubmission>";

        var result = new Filing13FXmlParser().ParseCoverPage(
            xml,
            accessionNumber: "0000886982-26-000274",
            cik: "0000886982",
            filingDate: new DateOnly(2026, 5, 15)
        );

        var manager = result.OtherManagers[1];
        manager.Name.Should().Be("GOLDMAN SACHS & CO. LLC");
        manager.Cik.Should().Be("769993");
        manager.Form13FFileNumber.Should().Be("028-00687");
        manager.CrdNumber.Should().Be("000000361");
    }

    [Fact]
    public void ParseCoverPage_OtherManagerWithNameOnly_KeepsTheEntryWithNullIdentifiers()
    {
        // Every identifier is optional in the SEC schema, and secFileNumber is absent from every
        // live filing seen so far. A parser that required one would drop real managers; one that
        // substituted an empty string would make an absent identifier look like a filed value and
        // let it be joined on. The entry survives, unlinkable, with nulls.
        var xml =
            "<edgarSubmission>"
            + "  <headerData><cik>0001067983</cik></headerData>"
            + "  <coverPage><reportCalendarOrQuarter>03-31-2026</reportCalendarOrQuarter></coverPage>"
            + "  <summaryPage>"
            + "    <otherManagers2Info>"
            + "      <otherManager2>"
            + "        <sequenceNumber>2</sequenceNumber>"
            + "        <otherManager><name>NAMELESS ADVISORS</name></otherManager>"
            + "      </otherManager2>"
            + "    </otherManagers2Info>"
            + "  </summaryPage>"
            + "</edgarSubmission>";

        var result = new Filing13FXmlParser().ParseCoverPage(
            xml,
            accessionNumber: "0001067983-26-000401",
            cik: "0001067983",
            filingDate: new DateOnly(2026, 5, 15)
        );

        var manager = result.OtherManagers[2];
        manager.Name.Should().Be("NAMELESS ADVISORS");
        manager.Cik.Should().BeNull();
        manager.Form13FFileNumber.Should().BeNull();
        manager.CrdNumber.Should().BeNull();
        manager.SecFileNumber.Should().BeNull();
    }

    [Fact]
    public void ParseCoverPage_BothManagerLists_KeepsThemApartAndDoesNotInvertEitherEdge()
    {
        // The two lists mean opposite things — the cover page names who reports FOR this filer,
        // the summary page names who it reports for — and a combination report carries both. They
        // also share the element name `otherManager`, so an unscoped scan folds them into one and
        // silently inverts half the relationships. This pins that they stay separate.
        var xml =
            "<edgarSubmission>"
            + "  <headerData><cik>0000895421</cik></headerData>"
            + "  <coverPage>"
            + "    <reportCalendarOrQuarter>03-31-2026</reportCalendarOrQuarter>"
            + "    <reportType>13F COMBINATION REPORT</reportType>"
            + "    <otherManagersInfo>"
            + "      <otherManager>"
            + "        <cik>0002026079</cik>"
            + "        <form13FFileNumber>028-24289</form13FFileNumber>"
            + "        <name>PARENT REPORTING FOR US</name>"
            + "      </otherManager>"
            + "    </otherManagersInfo>"
            + "  </coverPage>"
            + "  <summaryPage>"
            + "    <otherManagers2Info>"
            + "      <otherManager2>"
            + "        <sequenceNumber>1</sequenceNumber>"
            + "        <otherManager><name>SUBSIDIARY WE REPORT FOR</name></otherManager>"
            + "      </otherManager2>"
            + "    </otherManagers2Info>"
            + "  </summaryPage>"
            + "</edgarSubmission>";

        var result = new Filing13FXmlParser().ParseCoverPage(
            xml,
            accessionNumber: "0000895421-26-000183",
            cik: "0000895421",
            filingDate: new DateOnly(2026, 5, 15)
        );

        result.OtherManagers.Should().HaveCount(1);
        result.OtherManagers[1].Name.Should().Be("SUBSIDIARY WE REPORT FOR");

        result.CoverPageOtherManagers.Should().HaveCount(1);
        result.CoverPageOtherManagers[0].Name.Should().Be("PARENT REPORTING FOR US");
        result.CoverPageOtherManagers[0].Cik.Should().Be("2026079");
        result.CoverPageOtherManagers[0].Form13FFileNumber.Should().Be("028-24289");
    }

    [Fact]
    public void ParseCoverPage_NoCoverPageList_LeavesTheOppositeEdgeEmpty()
    {
        // Most 13F-HR filings carry no cover-page list at all. The summary page's own nested
        // <otherManager> elements must not be mistaken for one — that would invent a parent
        // relationship pointing at the filer's own subsidiaries.
        var xml =
            "<edgarSubmission>"
            + "  <headerData><cik>0000886982</cik></headerData>"
            + "  <coverPage><reportCalendarOrQuarter>03-31-2026</reportCalendarOrQuarter></coverPage>"
            + "  <summaryPage>"
            + "    <otherManagers2Info>"
            + "      <otherManager2>"
            + "        <sequenceNumber>1</sequenceNumber>"
            + "        <otherManager><name>A SUBSIDIARY</name></otherManager>"
            + "      </otherManager2>"
            + "    </otherManagers2Info>"
            + "  </summaryPage>"
            + "</edgarSubmission>";

        var result = new Filing13FXmlParser().ParseCoverPage(
            xml,
            accessionNumber: "0000886982-26-000274",
            cik: "0000886982",
            filingDate: new DateOnly(2026, 5, 15)
        );

        result.OtherManagers.Should().HaveCount(1);
        result.CoverPageOtherManagers.Should().BeEmpty();
    }
}
