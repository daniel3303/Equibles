using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.IntegrationTests.Holdings;

public class Realtime13FArchiveBuilderCoverPageIsAmendmentFalseTests
{
    // Sibling pin to BuildTests, which asserts COVERPAGE.ISAMENDMENT="Y" on
    // the IsAmendment=true arm. The false arm of the same ternary —
    // `filing.IsAmendment ? "Y" : "N"` — is read by the bulk-dataset import
    // path to decide whether to apply amendment delete-by-period. A regression
    // that emitted anything other than "N" for originals (empty string, "0",
    // "false") would parse as "not Y" downstream and the original would still
    // ingest correctly, *until* the importer's amendment gate flips on the
    // bare-presence check — at which point originals get treated as
    // amendments and silently overwrite prior quarters.
    [Fact]
    public async Task Build_OriginalFiling_CoverPageIsAmendmentColumnIsLiteralN()
    {
        var original = new Parsed13FFiling
        {
            AccessionNumber = "0000000003-26-000003",
            Cik = "1234567",
            FilingDate = new DateOnly(2026, 5, 1),
            PeriodOfReport = new DateOnly(2026, 3, 31),
            IsAmendment = false,
            FilingManagerName = "ORIGINAL FILER LLC",
            City = "OMAHA",
            StateOrCountry = "NE",
            Form13FFileNumber = "028-11111",
            CrdNumber = "271828",
        };

        using var archive = new Realtime13FArchiveBuilder().Build([original]);
        var entry = archive.GetEntry("COVERPAGE.tsv");
        entry.Should().NotBeNull();

        var rows = new List<Dictionary<string, string>>();
        await foreach (var row in new TsvParser().ParseEntry(entry))
            rows.Add(row);

        rows.Should().ContainSingle();
        rows[0]["ISAMENDMENT"].Should().Be("N");
    }
}
