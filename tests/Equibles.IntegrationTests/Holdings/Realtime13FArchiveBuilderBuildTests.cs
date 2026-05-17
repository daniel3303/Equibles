using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.IntegrationTests.Holdings;

public class Realtime13FArchiveBuilderBuildTests
{
    [Fact]
    public async Task Build_FilingManagerNameWithTabAndNewline_DoesNotCorruptTsvRow()
    {
        // Contract: the builder projects filings into the bulk-dataset TSV
        // layout so they flow through the same TsvParser the importer uses.
        // A free-text field (manager name) with an embedded TAB or NEWLINE
        // must NOT shift columns or inject a phantom row — otherwise every
        // trailing column is misread and reconciliation silently corrupts.
        // Verify by re-parsing COVERPAGE.tsv with the real TsvParser.
        var filing = new Parsed13FFiling
        {
            AccessionNumber = "0001067983-26-000300",
            Cik = "1067983",
            FilingDate = new DateOnly(2026, 5, 15),
            PeriodOfReport = new DateOnly(2026, 3, 31),
            IsAmendment = true,
            FilingManagerName = "EVIL\tADVISORS\nLLC",
            City = "OMAHA",
            StateOrCountry = "NE",
            Form13FFileNumber = "028-99999",
            CrdNumber = "314159",
            Holdings =
            [
                new Parsed13FHolding
                {
                    Cusip = "037833100",
                    TitleOfClass = "COM",
                    ShareType = "SH",
                    Shares = 1000,
                    InvestmentDiscretion = "SOLE",
                },
            ],
        };

        using var archive = new Realtime13FArchiveBuilder().Build([filing]);
        var entry = archive.GetEntry("COVERPAGE.tsv");
        entry.Should().NotBeNull();

        var rows = new List<Dictionary<string, string>>();
        await foreach (var row in new TsvParser().ParseEntry(entry))
            rows.Add(row);

        rows.Should().HaveCount(1);
        var coverPage = rows[0];
        coverPage["FILINGMANAGER_NAME"].Should().Be("EVIL ADVISORS LLC");
        coverPage["ISAMENDMENT"].Should().Be("Y");
        coverPage["FORM13FFILENUMBER"].Should().Be("028-99999");
        coverPage["CRDNUMBER"].Should().Be("314159");
    }
}
