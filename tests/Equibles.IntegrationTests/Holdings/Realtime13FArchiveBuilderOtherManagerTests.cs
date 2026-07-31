using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.IntegrationTests.Holdings;

public class Realtime13FArchiveBuilderOtherManagerTests
{
    [Fact]
    public async Task Build_OtherManagerNameWithTabAndNewline_DoesNotCorruptOtherManagerRow()
    {
        // Clean()'s contract — "any [tab/newline] embedded in a free-text field
        // would corrupt the whole row" — is pinned for COVERPAGE and INFOTABLE,
        // but the OTHERMANAGER2 writer is the third Clean() consumer and has no
        // pin. OtherManager names come straight from attacker-shaped SEC XML; a
        // refactor dropping Clean() around `name` would compile cleanly and
        // silently shift the NAME column or inject a phantom row that the
        // downstream importer reads as a real co-manager.
        var filing = new Parsed13FFiling
        {
            AccessionNumber = "0001067983-26-000401",
            Cik = "1067983",
            FilingDate = new DateOnly(2026, 5, 15),
            PeriodOfReport = new DateOnly(2026, 3, 31),
            IsAmendment = false,
            FilingManagerName = "BIG FUND",
            City = "OMAHA",
            StateOrCountry = "NE",
            Form13FFileNumber = "028-1",
            CrdNumber = "111",
            OtherManagers = new Dictionary<int, OtherManagerIdentity>
            {
                [1] = new OtherManagerIdentity(
                    "EVIL\tADVISORS\nLLC",
                    // The identifiers are free-text out of the same XML and go through the same
                    // writer, so each is a row-corrupting field in its own right.
                    "12\t345",
                    "028-\n1",
                    "999",
                    null
                ),
            },
        };

        using var archive = new Realtime13FArchiveBuilder().Build([filing]);
        var entry = archive.GetEntry("OTHERMANAGER2.tsv");
        entry.Should().NotBeNull();

        var rows = new List<Dictionary<string, string>>();
        await foreach (var row in new TsvParser().ParseEntry(entry))
            rows.Add(row);

        rows.Should().HaveCount(1, "the tab/newline must not inject a phantom row");
        var manager = rows[0];
        manager["ACCESSION_NUMBER"].Should().Be("0001067983-26-000401");
        manager["SEQUENCENUMBER"].Should().Be("1");
        manager["CIK"].Should().Be("12 345");
        manager["FORM13FFILENUMBER"].Should().Be("028- 1");
        manager["CRDNUMBER"].Should().Be("999");
        manager["NAME"].Should().Be("EVIL ADVISORS LLC");
    }

    [Fact]
    public async Task Build_CoverPageOtherManagers_WritesTheOppositeEdgeToItsOwnSection()
    {
        // The cover-page list is the child→parent edge and must land in OTHERMANAGER.tsv, not be
        // folded into the summary page's OTHERMANAGER2.tsv — the two mean opposite things, so a
        // writer that merged them would invert half the relationships the importer stores. The
        // list files no sequence numbers, so the surrogate key is positional.
        var filing = new Parsed13FFiling
        {
            AccessionNumber = "0000769993-26-000034",
            Cik = "769993",
            FilingDate = new DateOnly(2026, 5, 15),
            PeriodOfReport = new DateOnly(2026, 3, 31),
            FilingManagerName = "GOLDMAN SACHS & CO. LLC",
            CoverPageOtherManagers =
            [
                new OtherManagerIdentity(
                    "GOLDMAN SACHS GROUP INC",
                    "886982",
                    "028-04981",
                    null,
                    null
                ),
            ],
        };

        using var archive = new Realtime13FArchiveBuilder().Build([filing]);

        var coverEntry = archive.GetEntry("OTHERMANAGER.tsv");
        coverEntry.Should().NotBeNull();

        var rows = new List<Dictionary<string, string>>();
        await foreach (var row in new TsvParser().ParseEntry(coverEntry))
            rows.Add(row);

        rows.Should().HaveCount(1);
        rows[0]["ACCESSION_NUMBER"].Should().Be("0000769993-26-000034");
        rows[0]["OTHERMANAGER_SK"].Should().Be("1");
        rows[0]["CIK"].Should().Be("886982");
        rows[0]["FORM13FFILENUMBER"].Should().Be("028-04981");
        rows[0]["NAME"].Should().Be("GOLDMAN SACHS GROUP INC");

        // The summary-page section stays empty: this filing reports for nobody.
        var summaryRows = new List<Dictionary<string, string>>();
        await foreach (var row in new TsvParser().ParseEntry(archive.GetEntry("OTHERMANAGER2.tsv")))
            summaryRows.Add(row);
        summaryRows.Should().BeEmpty();
    }
}
