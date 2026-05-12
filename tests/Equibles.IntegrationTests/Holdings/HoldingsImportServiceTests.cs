using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.IntegrationTests.Holdings;

public class HoldingsImportServiceTests {
    [Fact]
    public void DeduplicateSubmissions_AmendmentSupersedingOriginal_KeepsOnlyLatestPerCikAndPeriod() {
        // The 13F-HR/A workflow on SEC EDGAR routinely produces multiple submissions for the
        // same (Cik, PeriodOfReport) — the original 13F-HR plus one or more later 13F-HR/A
        // amendments. HoldingsImportService.DeduplicateSubmissions is the single place that
        // collapses these to the latest filing per holder+quarter; everything downstream
        // (BuildCusipMapping, BuildPriceMap, UpsertInstitutionalHolders, HandleAmendments,
        // StreamAndInsertHoldings) assumes that collapse already happened. If a regression
        // ever kept both the original and the amendment, the InfoTable pass would double-count
        // every share for amended filings.
        //
        // This fact builds the smallest fixture that exercises both legs of the algorithm:
        // a duplicate pair where the second filing supersedes the first, and an unrelated
        // submission that must NOT be touched. Three observable post-conditions, together:
        //   (1) the older-FilingDate accession is removed,
        //   (2) the newer-FilingDate accession survives,
        //   (3) a submission with a different Cik is preserved even when it shares the
        //       same FilingDate as one of the duplicates.
        //
        // FilingDate is a *string* on SubmissionRow (lifted verbatim from the SEC TSV), so
        // the OrderByDescending compare runs on string ordering — the test uses ISO yyyy-MM-dd
        // strings to keep that compare meaningful.
        var context = new ImportContext {
            Submissions = new Dictionary<string, SubmissionRow>(StringComparer.OrdinalIgnoreCase) {
                ["ACC-001"] = new() {
                    AccessionNumber = "ACC-001",
                    Cik = "0001234567",
                    PeriodOfReport = "2024-09-30",
                    FilingDate = "2024-10-15",
                    FormType = "13F-HR",
                },
                ["ACC-002"] = new() {
                    AccessionNumber = "ACC-002",
                    Cik = "0001234567",
                    PeriodOfReport = "2024-09-30",
                    FilingDate = "2024-11-01",
                    FormType = "13F-HR/A",
                },
                ["ACC-003"] = new() {
                    AccessionNumber = "ACC-003",
                    Cik = "0009999999",
                    PeriodOfReport = "2024-09-30",
                    FilingDate = "2024-10-15",
                    FormType = "13F-HR",
                },
            },
        };

        HoldingsImportService.DeduplicateSubmissions(context);

        context.Submissions.Should().HaveCount(2);
        context.Submissions.Should().ContainKey("ACC-002")
            .WhoseValue.FilingDate.Should().Be("2024-11-01");
        context.Submissions.Should().ContainKey("ACC-003")
            .WhoseValue.Cik.Should().Be("0009999999");
        context.Submissions.Should().NotContainKey("ACC-001");
    }

    [Fact]
    public void DeduplicateSubmissions_RowsWithEmptyCikOrPeriod_NotGroupedAndSurvive() {
        // The dedupe grouper filters incomplete rows out of the group-by step:
        //   `.Where(s => !string.IsNullOrEmpty(s.Cik) && !string.IsNullOrEmpty(s.PeriodOfReport))`
        // Without that filter, every malformed submission (Cik="" or PeriodOfReport="") would
        // share the synthetic group key `"|"` and silently supersede each other. Real-world
        // SEC TSVs occasionally ship submissions with missing fields — paper filings, EDGAR
        // backfill bugs — and they MUST flow through dedupe unchanged so downstream phases
        // can decide what to do with them (typically: skip and report). A regression that
        // dropped the IsNullOrEmpty filter would quietly delete every malformed submission
        // except one per malformed-key bucket.
        //
        // This `[Fact]` ships three submissions whose only flaw is missing pieces: empty
        // Cik, empty PeriodOfReport, both empty. None share a (Cik, PeriodOfReport) key, so
        // even with the filter, none should be touched. Asserts all three survive.
        var context = new ImportContext {
            Submissions = new Dictionary<string, SubmissionRow>(StringComparer.OrdinalIgnoreCase) {
                ["ACC-NO-CIK"] = new() {
                    AccessionNumber = "ACC-NO-CIK",
                    Cik = "",
                    PeriodOfReport = "2024-09-30",
                    FilingDate = "2024-10-15",
                    FormType = "13F-HR",
                },
                ["ACC-NO-PERIOD"] = new() {
                    AccessionNumber = "ACC-NO-PERIOD",
                    Cik = "0001234567",
                    PeriodOfReport = "",
                    FilingDate = "2024-10-15",
                    FormType = "13F-HR",
                },
                ["ACC-BOTH-EMPTY"] = new() {
                    AccessionNumber = "ACC-BOTH-EMPTY",
                    Cik = "",
                    PeriodOfReport = "",
                    FilingDate = "2024-10-15",
                    FormType = "13F-HR",
                },
            },
        };

        HoldingsImportService.DeduplicateSubmissions(context);

        context.Submissions.Should().HaveCount(3);
        context.Submissions.Should().ContainKeys("ACC-NO-CIK", "ACC-NO-PERIOD", "ACC-BOTH-EMPTY");
    }
}
