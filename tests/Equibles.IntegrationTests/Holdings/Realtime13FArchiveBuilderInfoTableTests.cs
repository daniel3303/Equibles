using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.IntegrationTests.Holdings;

public class Realtime13FArchiveBuilderInfoTableTests
{
    [Fact]
    public async Task Build_HoldingTitleOfClassWithTabAndNewline_DoesNotCorruptInfoTableRow()
    {
        // The existing builder pin only proves the Clean() defense on the
        // cover-page path. Holdings fields come straight from attacker-shaped
        // SEC XML; an embedded TAB/NEWLINE in TitleOfClass must NOT shift
        // INFOTABLE columns (mis-reading CUSIP/shares) or inject a phantom row.
        // Verify by re-parsing INFOTABLE.tsv with the real TsvParser.
        var filing = new Parsed13FFiling
        {
            AccessionNumber = "0001067983-26-000400",
            Cik = "1067983",
            FilingDate = new DateOnly(2026, 5, 15),
            PeriodOfReport = new DateOnly(2026, 3, 31),
            IsAmendment = false,
            FilingManagerName = "BIG FUND",
            City = "OMAHA",
            StateOrCountry = "NE",
            Form13FFileNumber = "028-1",
            CrdNumber = "111",
            Holdings =
            [
                new Parsed13FHolding
                {
                    Cusip = "037833100",
                    TitleOfClass = "COM\tCLASS\nA",
                    ShareType = "SH",
                    Shares = 1234,
                    VotingAuthSole = 1234,
                    InvestmentDiscretion = "SOLE",
                },
            ],
        };

        using var archive = new Realtime13FArchiveBuilder().Build([filing]);
        var entry = archive.GetEntry("INFOTABLE.tsv");
        entry.Should().NotBeNull();

        var rows = new List<Dictionary<string, string>>();
        await foreach (var row in new TsvParser().ParseEntry(entry))
            rows.Add(row);

        rows.Should().HaveCount(1, "the tab/newline must not inject a phantom row");
        var holding = rows[0];
        holding["CUSIP"].Should().Be("037833100");
        holding["SSHPRNAMT"].Should().Be("1234");
        holding["TITLEOFCLASS"].Should().Be("COM CLASS A");
        holding["INVESTMENTDISCRETION"].Should().Be("SOLE");
    }
}
