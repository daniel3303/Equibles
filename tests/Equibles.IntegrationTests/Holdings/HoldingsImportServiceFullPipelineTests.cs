using System.Globalization;
using System.IO.Compression;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Exercises the heavy <see cref="HoldingsImportService.ImportDataSet"/> pipeline against a
/// real ParadeDB container so the FlexLabs <c>UpsertRange</c> + manager-entry attachment
/// path in <c>StreamAndInsertHoldings</c> / <c>FlushBatch</c> actually runs. The
/// in-memory EF Core provider doesn't support UpsertRange's <c>ON CONFLICT</c> SQL, so
/// these scenarios are unreachable from the sibling
/// <see cref="HoldingsImportServiceTests"/> file (which covers the four early-exit
/// branches before that path).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsImportServiceFullPipelineTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public HoldingsImportServiceFullPipelineTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
        _previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
    }

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        CultureInfo.CurrentCulture = _previousCulture;
        return Task.CompletedTask;
    }

    private EquiblesDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    /// <summary>
    /// Builds an <see cref="IServiceScopeFactory"/> whose every <c>CreateScope()</c> call
    /// yields a fresh <see cref="EquiblesDbContext"/> bound to the same ParadeDB instance
    /// — mirroring production DI's scoped-DbContext lifetime. Each repository the
    /// importer pulls out of a scope therefore gets its own context, so saves don't
    /// fight for the same change-tracker.
    /// </summary>
    private IServiceScopeFactory CreateScopeFactory()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesDbContext)).Returns(ctx);
                sp.GetService(typeof(CommonStockRepository))
                    .Returns(new CommonStockRepository(ctx));
                sp.GetService(typeof(InstitutionalHolderRepository))
                    .Returns(new InstitutionalHolderRepository(ctx));
                sp.GetService(typeof(InstitutionalHoldingRepository))
                    .Returns(new InstitutionalHoldingRepository(ctx));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    private HoldingsImportService CreateImporter(IStockPriceProvider priceProvider)
    {
        return new HoldingsImportService(
            CreateScopeFactory(),
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            priceProvider
        );
    }

    private static ZipArchive BuildArchive(params (string Name, string Body)[] entries)
    {
        var buffer = new MemoryStream();
        using (var writer = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, body) in entries)
            {
                var entry = writer.CreateEntry(name);
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(body);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    private static IStockPriceProvider PriceProviderReturning(
        Dictionary<(Guid, DateOnly), decimal> prices
    )
    {
        var provider = Substitute.For<IStockPriceProvider>();
        provider
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(prices));
        return provider;
    }

    // ── Happy path ──────────────────────────────────────────────────────

    [Fact]
    public async Task ImportDataSet_FullyValidArchiveWithMatchingStockAndPrice_PersistsHoldingWithComputedValue()
    {
        // Exercises every phase of ImportDataSet end-to-end:
        //   ParseSubmissions → DeduplicateSubmissions → ParseCoverPages →
        //   BuildCusipMapping (DB lookup) → BuildPriceMap (provider call) →
        //   ParseOtherManagers (skipped, no tsv) → UpsertInstitutionalHolders (DB insert) →
        //   HandleAmendments (no-op, ISAMENDMENT=N) → StreamAndInsertHoldings → FlushBatch
        //     → FlexLabs UpsertRange ON CONFLICT (only runs against real Postgres)
        //     → manager-entry attach-back loop
        // Pin the canonical post-conditions on a real database row: holding present,
        // value = shares * price, ValuePending=false, a ManagerEntry persisted.
        // Regressions in any phase break at least one of these assertions.
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
            Cusip = "037833100",
        };
        using (var seed = FreshContext())
        {
            seed.Set<CommonStock>().Add(stock);
            await seed.SaveChangesAsync();
        }

        var reportDate = new DateOnly(2024, 9, 30);
        var submission =
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
            + "13F-HR\tACC-001\t2024-10-15\t2024-09-30\t0001067983\n";
        var coverPage =
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\tFILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\n"
            + "ACC-001\tN\tBerkshire Hathaway\tOmaha\tNE\t028-12345\t12345\n";
        var infoTable =
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\tVOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n"
            + "ACC-001\t037833100\t1000\tSH\t\tSOLE\t1000\t0\t0\tCOM\t\n";

        using var archive = BuildArchive(
            ("SUBMISSION.tsv", submission),
            ("COVERPAGE.tsv", coverPage),
            ("INFOTABLE.tsv", infoTable)
        );

        var prices = new Dictionary<(Guid, DateOnly), decimal> { [(stock.Id, reportDate)] = 150m };
        var sut = CreateImporter(PriceProviderReturning(prices));

        var result = await sut.ImportDataSet(
            archive,
            new DateOnly(2024, 1, 1),
            CancellationToken.None
        );

        result.SubmissionCount.Should().Be(1);
        result.IsComplete.Should().BeTrue();

        using var verify = FreshContext();
        var holding = await verify
            .Set<InstitutionalHolding>()
            .Include(h => h.ManagerEntries)
            .SingleAsync();
        holding.Shares.Should().Be(1000);
        holding.Value.Should().Be(150_000);
        holding.ValuePending.Should().BeFalse();
        holding.ShareType.Should().Be(ShareType.Shares);
        holding.OptionType.Should().BeNull();
        holding.InvestmentDiscretion.Should().Be(InvestmentDiscretion.Sole);
        holding.IsAmendment.Should().BeFalse();
        holding.Cusip.Should().Be("037833100");
        holding.AccessionNumber.Should().Be("ACC-001");
        holding.ManagerEntries.Should().ContainSingle().Which.Shares.Should().Be(1000);
        // UpsertInstitutionalHolders side-effect: the new CIK was inserted with cover-page metadata.
        var holder = await verify.Set<InstitutionalHolder>().SingleAsync(h => h.Cik == "1067983");
        holder.Name.Should().Be("Berkshire Hathaway");
        holder.City.Should().Be("Omaha");
    }

    // ── Cold-start CUSIP race (GH-817) ─────────────────────────────────

    [Fact]
    public async Task ImportDataSet_NoTrackedStockHasMatchingCusip_IsNotComplete_SoDataSetIsRetriedNotMarkedProcessed()
    {
        // GH-817: on a cold start the FTD scraper hasn't seeded CUSIPs yet, so
        // no tracked CommonStock has a Cusip and BuildCusipMapping maps nothing.
        // That must NOT be reported as a completed import — otherwise the
        // worker marks the data set processed and never backfills it once
        // CUSIPs are seeded. Contract: submissions still parse (SubmissionCount
        // > 0) but IsComplete is false, and nothing is persisted.
        // No CommonStock is seeded here (mirrors the cold-start DB state).
        var submission =
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
            + "13F-HR\tACC-777\t2026-05-15\t2026-03-31\t0001067983\n";
        var coverPage =
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\tFILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\n"
            + "ACC-777\tN\tBerkshire Hathaway\tOmaha\tNE\t028-12345\t12345\n";
        var infoTable =
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\tVOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n"
            + "ACC-777\t037833100\t1000\tSH\t\tSOLE\t1000\t0\t0\tCOM\t\n";

        using var archive = BuildArchive(
            ("SUBMISSION.tsv", submission),
            ("COVERPAGE.tsv", coverPage),
            ("INFOTABLE.tsv", infoTable)
        );

        var sut = CreateImporter(
            PriceProviderReturning(new Dictionary<(Guid, DateOnly), decimal>())
        );

        var result = await sut.ImportDataSet(
            archive,
            new DateOnly(2025, 1, 1),
            CancellationToken.None
        );

        result.SubmissionCount.Should().Be(1);
        result.IsComplete.Should().BeFalse();

        using var verify = FreshContext();
        (await verify.Set<InstitutionalHolding>().CountAsync()).Should().Be(0);
    }

    // ── Price missing ──────────────────────────────────────────────────

    [Fact]
    public async Task ImportDataSet_PriceProviderReturnsEmpty_PersistsHoldingAsValuePendingZero()
    {
        // Pins the BuildPriceMap miss → StreamAndInsertHoldings fallback:
        //   `var value = hasPrice ? (long)(shares * closePrice) : 0L;`
        //   `var valuePending = !hasPrice;`
        // A regression that flipped the bool (treating missing prices as found)
        // would silently insert holdings with Value=0 and ValuePending=false, so
        // the downstream HoldingsValueRecalculator would never see them and the
        // 0-dollar rows would persist permanently. Asserting BOTH fields proves
        // the fallback wired the two related properties consistently.
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "TSLA",
            Name = "Tesla",
            Cik = "0001318605",
            Cusip = "88160R101",
        };
        using (var seed = FreshContext())
        {
            seed.Set<CommonStock>().Add(stock);
            await seed.SaveChangesAsync();
        }

        var submission =
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
            + "13F-HR\tACC-002\t2024-10-15\t2024-09-30\t0001067983\n";
        var coverPage =
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\n"
            + "ACC-002\tN\tBerkshire Hathaway\n";
        var infoTable =
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tINVESTMENTDISCRETION\n"
            + "ACC-002\t88160R101\t500\tSH\tSOLE\n";

        using var archive = BuildArchive(
            ("SUBMISSION.tsv", submission),
            ("COVERPAGE.tsv", coverPage),
            ("INFOTABLE.tsv", infoTable)
        );

        var sut = CreateImporter(PriceProviderReturning([]));

        await sut.ImportDataSet(archive, new DateOnly(2024, 1, 1), CancellationToken.None);

        using var verify = FreshContext();
        var holding = await verify.Set<InstitutionalHolding>().SingleAsync();
        holding.Value.Should().Be(0);
        holding.ValuePending.Should().BeTrue();
        holding.Shares.Should().Be(500);
    }

    // ── ParseOtherManagers + ResolveManagerName end-to-end ────────────

    [Fact]
    public async Task ImportDataSet_InfoTableRowReferencesOtherManager_PersistsManagerEntryWithResolvedName()
    {
        // The INFOTABLE.OTHERMANAGER column carries the SEQUENCENUMBER of a co-filer
        // listed in OTHERMANAGER2.tsv. StreamAndInsertHoldings calls
        // ResolveManagerName(context, accession, otherManagerNumber) which looks up
        // OtherManagers[accession][seq]. This pin proves the OTHERMANAGER2.tsv pass
        // (ParseOtherManagers) actually populates that map and that the lookup
        // resolves to the right name in the persisted HoldingManagerEntry.
        // A regression that dropped ParseOtherManagers entirely would still pass
        // every early-exit pin (the orchestrator doesn't gate on it) but would
        // silently leave every co-filer-attributed holding with ManagerName=null.
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "MSFT",
            Name = "Microsoft",
            Cik = "0000789019",
            Cusip = "594918104",
        };
        using (var seed = FreshContext())
        {
            seed.Set<CommonStock>().Add(stock);
            await seed.SaveChangesAsync();
        }

        var submission =
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
            + "13F-HR\tACC-003\t2024-10-15\t2024-09-30\t0001067983\n";
        var coverPage =
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\n" + "ACC-003\tN\tPrimary Manager\n";
        var otherManagers =
            "ACCESSION_NUMBER\tSEQUENCENUMBER\tNAME\n" + "ACC-003\t2\tCo-Filer Capital LLC\n";
        var infoTable =
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tINVESTMENTDISCRETION\tOTHERMANAGER\n"
            + "ACC-003\t594918104\t750\tSH\tDFND\t2\n";

        using var archive = BuildArchive(
            ("SUBMISSION.tsv", submission),
            ("COVERPAGE.tsv", coverPage),
            ("OTHERMANAGER2.tsv", otherManagers),
            ("INFOTABLE.tsv", infoTable)
        );

        var sut = CreateImporter(PriceProviderReturning([]));

        await sut.ImportDataSet(archive, new DateOnly(2024, 1, 1), CancellationToken.None);

        using var verify = FreshContext();
        var holding = await verify
            .Set<InstitutionalHolding>()
            .Include(h => h.ManagerEntries)
            .SingleAsync();
        holding.InvestmentDiscretion.Should().Be(InvestmentDiscretion.Defined);
        holding
            .ManagerEntries.Should()
            .ContainSingle()
            .Which.Should()
            .Match<HoldingManagerEntry>(m =>
                m.ManagerNumber == 2 && m.ManagerName == "Co-Filer Capital LLC"
            );
    }

    // ── Duplicate aggregation inside StreamAndInsertHoldings ──────────

    [Fact]
    public async Task ImportDataSet_TwoInfoTableRowsShareCompositeKey_AggregatesIntoSingleHolding()
    {
        // Two INFOTABLE rows with identical
        //   (CommonStockId, InstitutionalHolderId, ReportDate, ShareType, OptionType)
        // are merged in StreamAndInsertHoldings via the `holdingsMap` and
        // `existing.Shares += shares` accumulator. This is the 13F-HR
        // multi-tranche scenario: a single filer reports the same security
        // across multiple INFOTABLE rows when they hold it under different
        // discretion categories or via co-filers. Without the aggregation
        // the FlushBatch upsert would still collapse them via ON CONFLICT,
        // but BOTH ManagerEntries would be lost (only the second row's
        // entries would survive the upsert's WhenMatched policy). Pinning
        // the aggregated row proves the in-memory merge fires before the
        // DB hop, preserving both manager entries.
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "NVDA",
            Name = "NVIDIA",
            Cik = "0001045810",
            Cusip = "67066G104",
        };
        using (var seed = FreshContext())
        {
            seed.Set<CommonStock>().Add(stock);
            await seed.SaveChangesAsync();
        }

        var submission =
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
            + "13F-HR\tACC-004\t2024-10-15\t2024-09-30\t0001067983\n";
        var coverPage =
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\n"
            + "ACC-004\tN\tBerkshire Hathaway\n";
        // Two SH rows with no OptionType — same composite key, must aggregate.
        var infoTable =
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tINVESTMENTDISCRETION\tVOTING_AUTH_SOLE\n"
            + "ACC-004\t67066G104\t300\tSH\tSOLE\t300\n"
            + "ACC-004\t67066G104\t700\tSH\tSOLE\t700\n";

        using var archive = BuildArchive(
            ("SUBMISSION.tsv", submission),
            ("COVERPAGE.tsv", coverPage),
            ("INFOTABLE.tsv", infoTable)
        );

        var sut = CreateImporter(
            PriceProviderReturning(
                new Dictionary<(Guid, DateOnly), decimal>
                {
                    [(stock.Id, new DateOnly(2024, 9, 30))] = 100m,
                }
            )
        );

        await sut.ImportDataSet(archive, new DateOnly(2024, 1, 1), CancellationToken.None);

        using var verify = FreshContext();
        var holdings = await verify
            .Set<InstitutionalHolding>()
            .Include(h => h.ManagerEntries)
            .ToListAsync();
        holdings.Should().ContainSingle();
        var aggregated = holdings[0];
        aggregated.Shares.Should().Be(1000);
        aggregated.Value.Should().Be(100_000);
        aggregated.VotingAuthSole.Should().Be(1000);
        aggregated.ManagerEntries.Should().HaveCount(2);
    }

    // ── HandleAmendments end-to-end ────────────────────────────────────

    [Fact]
    public async Task ImportDataSet_AmendmentFiling_DeletesPriorHoldingsForSameHolderAndPeriodBeforeInsert()
    {
        // The 13F-HR/A amendment workflow: a filer re-submits an entire quarter
        // to correct or replace prior reporting. HandleAmendments must delete
        // every prior InstitutionalHolding for (holder, reportDate) BEFORE
        // StreamAndInsertHoldings runs, otherwise the upsert would merge the
        // amendment's values with the original's instead of replacing them.
        //
        // The trickiest regression here is order: HandleAmendments fires
        // BEFORE StreamAndInsertHoldings. A refactor that reversed the order
        // would delete the amendment's own freshly-inserted rows instead of
        // the originals, producing an empty holdings table for the amended
        // quarter. Pinning a single-row "amendment replaces a different
        // shares count" scenario proves the order and the WHERE clause:
        //   - pre-seed an original with shares=999 marked under the same
        //     accession as the upcoming amendment is unsafe (the amendment's
        //     own AccessionNumber would mark it for deletion). Use a
        //     DIFFERENT original AccessionNumber so the test isolates the
        //     amendment's effect on the historical row.
        //   - after ImportDataSet the original row is gone, the amendment's
        //     row with shares=42 is the only survivor.
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "META",
            Name = "Meta",
            Cik = "0001326801",
            Cusip = "30303M102",
        };
        var holder = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "1067983",
            Name = "Berkshire Hathaway",
        };
        var reportDate = new DateOnly(2024, 9, 30);
        var originalHolding = new InstitutionalHolding
        {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            ReportDate = reportDate,
            FilingDate = new DateOnly(2024, 10, 1),
            Shares = 999,
            Value = 99_900,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = "ACC-ORIG",
            Cusip = "30303M102",
        };
        using (var seed = FreshContext())
        {
            seed.Set<CommonStock>().Add(stock);
            seed.Set<InstitutionalHolder>().Add(holder);
            seed.Set<InstitutionalHolding>().Add(originalHolding);
            await seed.SaveChangesAsync();
        }

        var submission =
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
            + "13F-HR/A\tACC-AMEND\t2024-11-15\t2024-09-30\t0001067983\n";
        var coverPage =
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\n"
            + "ACC-AMEND\tY\tBerkshire Hathaway\n";
        var infoTable =
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tINVESTMENTDISCRETION\n"
            + "ACC-AMEND\t30303M102\t42\tSH\tSOLE\n";

        using var archive = BuildArchive(
            ("SUBMISSION.tsv", submission),
            ("COVERPAGE.tsv", coverPage),
            ("INFOTABLE.tsv", infoTable)
        );

        var sut = CreateImporter(
            PriceProviderReturning(
                new Dictionary<(Guid, DateOnly), decimal> { [(stock.Id, reportDate)] = 200m }
            )
        );

        await sut.ImportDataSet(archive, new DateOnly(2024, 1, 1), CancellationToken.None);

        using var verify = FreshContext();
        var holdings = await verify
            .Set<InstitutionalHolding>()
            .Where(h => h.InstitutionalHolderId == holder.Id && h.ReportDate == reportDate)
            .ToListAsync();
        holdings
            .Should()
            .ContainSingle("amendment must replace, not merge with, the original holding");
        holdings[0].Shares.Should().Be(42);
        holdings[0].AccessionNumber.Should().Be("ACC-AMEND");
        holdings[0].IsAmendment.Should().BeTrue();
    }

    // ── Resilience / parser-branch coverage ─────────────────────────────

    [Fact]
    public async Task ImportDataSet_ArchiveMissingInfoTable_CompletesEmptyWithoutHoldings()
    {
        // BuildCusipMapping's "INFOTABLE.tsv not found" guard (zero-hit): an
        // archive with submissions but no holdings table is treated as
        // complete-but-empty (IsComplete=true so the worker marks it processed
        // and never retries forever), persisting zero holdings — not a crash.
        var submission =
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
            + "13F-HR\tACC-001\t2024-10-15\t2024-09-30\t0001067983\n";
        var coverPage =
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\tFILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\n"
            + "ACC-001\tN\tBerkshire Hathaway\tOmaha\tNE\t028-12345\t12345\n";

        using var archive = BuildArchive(
            ("SUBMISSION.tsv", submission),
            ("COVERPAGE.tsv", coverPage)
        );
        var sut = CreateImporter(PriceProviderReturning([]));

        var result = await sut.ImportDataSet(
            archive,
            new DateOnly(2024, 1, 1),
            CancellationToken.None
        );

        result.IsComplete.Should().BeTrue();
        result.SubmissionCount.Should().Be(1);
        using var verify = FreshContext();
        (await verify.Set<InstitutionalHolding>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportDataSet_OtherManager2WithMalformedRows_SkipsThemAndStillCompletes()
    {
        // ParseOtherManagers' three skip guards (zero-hit): unknown accession,
        // non-numeric SEQUENCENUMBER, and empty NAME must each be skipped while
        // a valid co-manager row is still parsed and the import completes.
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
            Cusip = "037833100",
        };
        using (var seed = FreshContext())
        {
            seed.Set<CommonStock>().Add(stock);
            await seed.SaveChangesAsync();
        }

        var reportDate = new DateOnly(2024, 9, 30);
        var submission =
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
            + "13F-HR\tACC-001\t2024-10-15\t2024-09-30\t0001067983\n";
        var coverPage =
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\tFILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\n"
            + "ACC-001\tN\tBerkshire Hathaway\tOmaha\tNE\t028-12345\t12345\n";
        var infoTable =
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\tVOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n"
            + "ACC-001\t037833100\t1000\tSH\t\tSOLE\t1000\t0\t0\tCOM\t\n";
        var otherManager =
            "ACCESSION_NUMBER\tSEQUENCENUMBER\tNAME\n"
            + "UNKNOWN-ACC\t1\tGhost Manager\n" // unknown accession → skip
            + "ACC-001\tnot-a-number\tBad Seq Manager\n" // bad SEQUENCENUMBER → skip
            + "ACC-001\t3\t\n" // empty NAME → skip
            + "ACC-001\t2\tValid Co-Manager\n"; // valid → parsed

        using var archive = BuildArchive(
            ("SUBMISSION.tsv", submission),
            ("COVERPAGE.tsv", coverPage),
            ("INFOTABLE.tsv", infoTable),
            ("OTHERMANAGER2.tsv", otherManager)
        );

        var prices = new Dictionary<(Guid, DateOnly), decimal> { [(stock.Id, reportDate)] = 150m };
        var sut = CreateImporter(PriceProviderReturning(prices));

        var result = await sut.ImportDataSet(
            archive,
            new DateOnly(2024, 1, 1),
            CancellationToken.None
        );

        // The malformed rows are skipped without aborting; the import completes.
        result.SubmissionCount.Should().Be(1);
        result.IsComplete.Should().BeTrue();
        using var verify = FreshContext();
        (await verify.Set<InstitutionalHolding>().CountAsync()).Should().Be(1);
    }

    // ── Unmapped CUSIP row skipped ──────────────────────────────────────

    [Fact]
    public async Task ImportDataSet_InfoTableRowWithUnmappedCusip_SkipsThatRowAndPersistsTheRest()
    {
        // One INFOTABLE row references a CUSIP a tracked stock holds; a second
        // references a CUSIP no tracked stock holds. The second must hit the
        // CusipMapping miss → totalSkipped++/continue, while the first still
        // persists — a single unknown holding can't drop the rest of a filing.
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
            Cusip = "037833100",
        };
        using (var seed = FreshContext())
        {
            seed.Set<CommonStock>().Add(stock);
            await seed.SaveChangesAsync();
        }

        var reportDate = new DateOnly(2024, 9, 30);
        var submission =
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
            + "13F-HR\tACC-001\t2024-10-15\t2024-09-30\t0001067983\n";
        var coverPage =
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\tFILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\n"
            + "ACC-001\tN\tBerkshire Hathaway\tOmaha\tNE\t028-12345\t12345\n";
        var infoTable =
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\tVOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n"
            + "ACC-001\t037833100\t1000\tSH\t\tSOLE\t1000\t0\t0\tCOM\t\n"
            // CUSIP no tracked stock holds → CusipMapping miss → row skipped.
            + "ACC-001\t999999999\t500\tSH\t\tSOLE\t500\t0\t0\tCOM\t\n";

        using var archive = BuildArchive(
            ("SUBMISSION.tsv", submission),
            ("COVERPAGE.tsv", coverPage),
            ("INFOTABLE.tsv", infoTable)
        );

        var prices = new Dictionary<(Guid, DateOnly), decimal> { [(stock.Id, reportDate)] = 150m };
        var sut = CreateImporter(PriceProviderReturning(prices));

        var result = await sut.ImportDataSet(
            archive,
            new DateOnly(2024, 1, 1),
            CancellationToken.None
        );

        result.IsComplete.Should().BeTrue();
        using var verify = FreshContext();
        var holdings = await verify.Set<InstitutionalHolding>().ToListAsync();
        holdings
            .Should()
            .ContainSingle("the unmapped-CUSIP row is skipped, the mapped one persists");
        holdings[0].Cusip.Should().Be("037833100");
    }
}
