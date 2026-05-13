using System.IO.Compression;
using System.Text;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

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

    // ── ImportDataSet orchestrator: early-exit branches ──────────────────
    //
    // The four pins below pin the four return points BEFORE the heavy
    // StreamAndInsertHoldings + FlushBatch + UpsertRange path that the
    // EF Core InMemory provider can't execute. Together they cover every
    // structural / data-shape early-exit ImportDataSet can take, which is
    // exactly the region most likely to silently change shape during a
    // refactor of the orchestrator's bool/null return signaling.

    private static readonly IModuleConfiguration[] Modules = [
        new CommonStocksModuleConfiguration(),
        new HoldingsModuleConfiguration(),
    ];

    private static EquiblesDbContext CreateDb() => TestDbContextFactory.Create(Modules);

    private static IServiceScopeFactory ScopeFactoryFor(EquiblesDbContext db) {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ => {
            var sp = Substitute.For<IServiceProvider>();
            sp.GetService(typeof(CommonStockRepository)).Returns(new CommonStockRepository(db));
            sp.GetService(typeof(InstitutionalHolderRepository)).Returns(new InstitutionalHolderRepository(db));
            sp.GetService(typeof(InstitutionalHoldingRepository)).Returns(new InstitutionalHoldingRepository(db));
            sp.GetService(typeof(EquiblesDbContext)).Returns(db);
            var scope = Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(sp);
            return scope;
        });
        return scopeFactory;
    }

    private static HoldingsImportService CreateImporter(EquiblesDbContext db) {
        return new HoldingsImportService(
            ScopeFactoryFor(db),
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            Substitute.For<IStockPriceProvider>());
    }

    private static ZipArchive BuildArchive(params (string Name, string Body)[] entries) {
        var buffer = new MemoryStream();
        using (var writer = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true)) {
            foreach (var (name, body) in entries) {
                var entry = writer.CreateEntry(name);
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(body);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    [Fact]
    public async Task ImportDataSet_ArchiveMissingSubmissionTsv_ReturnsZeroSubmissionsNotComplete() {
        // ParseSubmissions returns null when SUBMISSION.tsv is absent. The
        // ImportDataSet orchestrator must convert that null into
        // (SubmissionCount: 0, IsComplete: false) so the scraper schedules
        // the file for retry next cycle rather than marking it processed.
        // A regression that flipped IsComplete to true on the null path
        // (a "simplification" treating no-submissions and no-archive as
        // equivalent) would silently consume the SEC dataset's
        // ProcessedDataSet slot without importing a single holding —
        // the gap would persist forever once marked.
        using var db = CreateDb();
        using var archive = BuildArchive(("UNRELATED.tsv", "header\nbody"));
        var sut = CreateImporter(db);

        var result = await sut.ImportDataSet(archive, new DateOnly(2020, 1, 1), CancellationToken.None);

        result.SubmissionCount.Should().Be(0);
        result.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task ImportDataSet_SubmissionTsvWithNoMatching13FHRRows_ReturnsZeroSubmissionsComplete() {
        // Structurally distinct from the missing-tsv path: here SUBMISSION.tsv
        // exists but contains only filings the importer filters out (wrong
        // form type, missing accession, or PeriodOfReport < MinReportDate).
        // ParseSubmissions returns false on the empty-after-filter path and
        // ImportDataSet maps that to IsComplete:true — the dataset really
        // contained nothing for us, and a retry would only fetch the same
        // empty filter result. Pinning this distinguishes "no data here,
        // mark done" (false) from "structurally broken, retry" (null —
        // sibling pin above).
        var tsv = "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n" +
                  // Wrong form type → filtered
                  "10-K\tACC-001\t2024-01-15\t2024-09-30\t0001234567\n" +
                  // Empty accession → filtered
                  "13F-HR\t\t2024-01-15\t2024-09-30\t0001234567\n" +
                  // PeriodOfReport before MinReportDate → filtered
                  "13F-HR\tACC-002\t2024-01-15\t2019-09-30\t0001234567\n";

        using var db = CreateDb();
        using var archive = BuildArchive(("SUBMISSION.tsv", tsv));
        var sut = CreateImporter(db);

        var result = await sut.ImportDataSet(archive, new DateOnly(2024, 1, 1), CancellationToken.None);

        result.SubmissionCount.Should().Be(0);
        result.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task ImportDataSet_SubmissionsParseButCoverPageTsvMissing_ReturnsParsedCountNotComplete() {
        // ParseCoverPages returns false when COVERPAGE.tsv is absent. The
        // orchestrator must propagate that as (count, IsComplete:false) —
        // we already know how many submissions parsed, but without cover
        // pages we can't enrich institutional holders, so the dataset is
        // half-imported and must retry. A regression here is the most
        // dangerous of the four: marking IsComplete:true would persist
        // the submission count to ProcessedDataSet but skip the entire
        // cover-page enrichment step on every future cycle for that file.
        var tsv = "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n" +
                  "13F-HR\tACC-001\t2024-10-15\t2024-09-30\t0001234567\n";

        using var db = CreateDb();
        using var archive = BuildArchive(("SUBMISSION.tsv", tsv));
        var sut = CreateImporter(db);

        var result = await sut.ImportDataSet(archive, new DateOnly(2024, 1, 1), CancellationToken.None);

        result.SubmissionCount.Should().Be(1);
        result.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task ImportDataSet_NoCusipsInInfoTableMatchTrackedStocks_ReturnsParsedCountComplete() {
        // BuildCusipMapping returns false when none of the CUSIPs in
        // INFOTABLE.tsv match a tracked CommonStock. ImportDataSet maps
        // this to (count, IsComplete:true) — the file was structurally
        // sound and we just don't track any of its issuers, so retrying
        // would produce the same empty mapping. This is the inverse of
        // the COVERPAGE.tsv-missing pin above (true vs false IsComplete
        // for the same SubmissionCount), and a swap regression between
        // them would corrupt ProcessedDataSet bookkeeping in opposite
        // directions for opposite root causes.
        var submissionTsv = "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n" +
                            "13F-HR\tACC-001\t2024-10-15\t2024-09-30\t0001234567\n";
        var coverPageTsv = "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\n" +
                           "ACC-001\tN\tTest Manager\n";
        var infoTableTsv = "ACCESSION_NUMBER\tCUSIP\n" +
                           "ACC-001\t999999999\n"; // CUSIP not in DB

        using var db = CreateDb();
        db.Set<CommonStock>().Add(new CommonStock {
            Id = Guid.NewGuid(), Ticker = "AAPL", Name = "Apple", Cusip = "037833100",
        });
        await db.SaveChangesAsync();

        using var archive = BuildArchive(
            ("SUBMISSION.tsv", submissionTsv),
            ("COVERPAGE.tsv", coverPageTsv),
            ("INFOTABLE.tsv", infoTableTsv));
        var sut = CreateImporter(db);

        var result = await sut.ImportDataSet(archive, new DateOnly(2024, 1, 1), CancellationToken.None);

        result.SubmissionCount.Should().Be(1);
        result.IsComplete.Should().BeTrue();
    }
}
