using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Holdings;

public class Realtime13FArchiveBuilderSubmissionTests
{
    // Pins the SUBMISSION.tsv shape — specifically the SUBMISSIONTYPE column
    // emitted by the amendment branch: 13F-HR/A for IsAmendment=true, 13F-HR
    // otherwise. The bulk dataset's import path keys on this string; a regression
    // would either drop amendments (no rebase) or treat originals as amendments
    // (silent delete-by-period). Re-parses with the real TsvParser the importer uses.
    [Fact]
    public async Task Build_OneAmendmentOneOriginal_SubmissionTsvHasCorrectSubmissionTypes()
    {
        var amendment = new Parsed13FFiling
        {
            AccessionNumber = "0000000001-26-000001",
            Cik = "1234567",
            FilingDate = new DateOnly(2026, 5, 1),
            PeriodOfReport = new DateOnly(2026, 3, 31),
            IsAmendment = true,
        };
        var original = new Parsed13FFiling
        {
            AccessionNumber = "0000000002-26-000002",
            Cik = "7654321",
            FilingDate = new DateOnly(2026, 5, 2),
            PeriodOfReport = new DateOnly(2026, 3, 31),
            IsAmendment = false,
        };

        using var archive = new Realtime13FArchiveBuilder().Build([amendment, original]);
        var entry = archive.GetEntry("SUBMISSION.tsv");
        entry.Should().NotBeNull();

        var rows = new List<Dictionary<string, string>>();
        await foreach (var row in new TsvParser().ParseEntry(entry))
            rows.Add(row);

        rows.Should().HaveCount(2);
        rows[0]["SUBMISSIONTYPE"].Should().Be("13F-HR/A");
        rows[0]["ACCESSION_NUMBER"].Should().Be("0000000001-26-000001");
        rows[1]["SUBMISSIONTYPE"].Should().Be("13F-HR");
        rows[1]["ACCESSION_NUMBER"].Should().Be("0000000002-26-000002");
    }
}
