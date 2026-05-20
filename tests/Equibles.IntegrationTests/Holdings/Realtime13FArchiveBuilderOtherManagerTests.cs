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
            OtherManagers = new Dictionary<int, string> { [1] = "EVIL\tADVISORS\nLLC" },
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
        manager["NAME"].Should().Be("EVIL ADVISORS LLC");
    }
}
