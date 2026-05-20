using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.IntegrationTests.Holdings;

public class Realtime13FArchiveBuilderCarriageReturnNameTests
{
    [Fact]
    public async Task Build_FilingManagerNameWithBareCarriageReturn_DoesNotCorruptTsvRow()
    {
        // The doc-comment on Realtime13FArchiveBuilder.Clean states that
        // TAB and NEWLINE characters in free-text fields would corrupt the
        // bulk-dataset TSV row and are therefore replaced with spaces. The
        // existing tab+newline pin
        // (Realtime13FArchiveBuilderBuildTests.Build_FilingManagerNameWithTabAndNewline_…)
        // exercises the `\t` and `\n` arms of Clean in combination; the lone
        // `\r` arm has no pin. Classic-Mac line endings (and stray CRs from
        // copy-paste through Windows clipboards) reach the production path
        // via SEC's free-text manager-name field — without the `\r`
        // substitution the COVERPAGE.tsv row would carry a bare CR through
        // to the TsvParser, where it can either inject a phantom row break
        // or simply leak into a downstream consumer that reads the field
        // verbatim. A refactor that "tidies up" the `Replace('\r', ' ')`
        // under the false intuition that "lone CR is impossible because
        // every modern feed uses LF" would compile, pass the existing
        // tab+newline pin, and silently regress this arm.
        //
        // Pin the lone-CR case: an input of "FUND\rNAME" must produce a
        // single COVERPAGE.tsv row whose FILINGMANAGER_NAME contains no
        // carriage return (the existing pin's assertion style matches the
        // exact substituted value, so do the same here — "FUND NAME").
        var filing = new Parsed13FFiling
        {
            AccessionNumber = "0001067983-26-000500",
            Cik = "1067983",
            FilingDate = new DateOnly(2026, 5, 15),
            PeriodOfReport = new DateOnly(2026, 3, 31),
            IsAmendment = false,
            FilingManagerName = "FUND\rNAME",
            City = "OMAHA",
            StateOrCountry = "NE",
            Form13FFileNumber = "028-1",
            CrdNumber = "111",
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

        rows.Should().HaveCount(1, "a bare CR must not inject a phantom row");
        var coverPage = rows[0];
        coverPage["FILINGMANAGER_NAME"].Should().Be("FUND NAME");
        coverPage["FILINGMANAGER_NAME"].Should().NotContain("\r");
    }
}
