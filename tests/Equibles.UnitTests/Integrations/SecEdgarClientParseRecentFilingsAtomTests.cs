using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Integrations;

/// <summary>
/// Pins the "Latest Filings" ATOM parsing: entry shape lifted from a live feed
/// capture. Malformed entries must be skipped (never fail the page) because a
/// dropped page would blind the realtime discovery layer for a full poll.
/// </summary>
public class SecEdgarClientParseRecentFilingsAtomTests
{
    private const string FeedXml = """
        <?xml version="1.0" encoding="ISO-8859-1" ?>
        <feed xmlns="http://www.w3.org/2005/Atom">
        <title>Latest Filings - Thu, 09 Jul 2026 10:16:48 EDT</title>
        <entry>
        <title>144 - Camerana Niccolo (0001995137) (Reporting)</title>
        <link rel="alternate" type="text/html" href="https://www.sec.gov/Archives/edgar/data/1995137/000199513726000012/0001995137-26-000012-index.htm"/>
        <updated>2026-07-09T10:16:01-04:00</updated>
        <category scheme="https://www.sec.gov/" label="form type" term="144"/>
        <id>urn:tag:sec.gov,2008:accession-number=0001995137-26-000012</id>
        </entry>
        <entry>
        <title>144 - Scorpio Tankers Inc. (0001483934) (Subject)</title>
        <link rel="alternate" type="text/html" href="https://www.sec.gov/Archives/edgar/data/1483934/000199513726000012/0001995137-26-000012-index.htm"/>
        <updated>2026-07-09T10:16:01-04:00</updated>
        <category scheme="https://www.sec.gov/" label="form type" term="144"/>
        <id>urn:tag:sec.gov,2008:accession-number=0001995137-26-000012</id>
        </entry>
        <entry>
        <title>424B2 - HSBC USA INC /MD/ (0000083246) (Filer)</title>
        <updated>2026-07-09T10:15:37-04:00</updated>
        <category scheme="https://www.sec.gov/" label="form type" term="424B2"/>
        <id>urn:tag:sec.gov,2008:accession-number=0001104659-26-082152</id>
        </entry>
        <entry>
        <title>no cik in this title</title>
        <updated>2026-07-09T10:15:00-04:00</updated>
        <category scheme="https://www.sec.gov/" label="form type" term="8-K"/>
        <id>urn:tag:sec.gov,2008:accession-number=0001104659-26-082153</id>
        </entry>
        </feed>
        """;

    [Fact]
    public void ParseRecentFilingsAtom_ParsesWellFormedEntries()
    {
        var entries = SecEdgarClient.ParseRecentFilingsAtom(FeedXml);

        entries.Should().HaveCount(3);

        var first = entries[0];
        first.Cik.Should().Be("0001995137");
        first.FormType.Should().Be("144");
        first.AccessionNumber.Should().Be("0001995137-26-000012");
        first.CompanyName.Should().Be("Camerana Niccolo");
        first
            .Updated.Should()
            .Be(new DateTimeOffset(2026, 7, 9, 10, 16, 1, TimeSpan.FromHours(-4)));
    }

    [Fact]
    public void ParseRecentFilingsAtom_SameAccessionKeepsOneEntryPerEntity()
    {
        var entries = SecEdgarClient.ParseRecentFilingsAtom(FeedXml);

        var sameAccession = entries
            .Where(e => e.AccessionNumber == "0001995137-26-000012")
            .ToList();

        // A Form 144 lists both the reporting person and the subject company —
        // the subject side is what maps the filing to a tracked issuer.
        sameAccession.Should().HaveCount(2);
        sameAccession.Select(e => e.Cik).Should().Contain(["0001995137", "0001483934"]);
    }

    [Fact]
    public void ParseRecentFilingsAtom_EntryWithoutParseableCik_IsSkipped()
    {
        var entries = SecEdgarClient.ParseRecentFilingsAtom(FeedXml);

        entries.Should().NotContain(e => e.AccessionNumber == "0001104659-26-082153");
    }

    [Fact]
    public void ParseRecentFilingsAtom_EmptyPayload_ReturnsEmpty()
    {
        SecEdgarClient.ParseRecentFilingsAtom("").Should().BeEmpty();
        SecEdgarClient.ParseRecentFilingsAtom(null).Should().BeEmpty();
    }
}
