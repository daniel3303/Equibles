using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

[Collection(ParadeDbCollection.Name)]
public class HoldingsImportFailureRecoveryTests(ParadeDbFixture fixture) : IAsyncLifetime
{
    private static readonly DateOnly Quarter = new(2026, 6, 30);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Failure_SurvivesNewContext_AndReopensAfterResolution()
    {
        await using (var first = fixture.CreateDbContext())
            await new HoldingsImportFailureRepository(first).Record(
                "original",
                "123",
                Quarter,
                Quarter,
                HoldingsImportFailureReason.IdentityConflict,
                CancellationToken.None
            );
        await using var second = fixture.CreateDbContext();
        var repo = new HoldingsImportFailureRepository(second);
        var row = await repo.GetAll().AsNoTracking().SingleAsync();
        row.ResolvedAt.Should().BeNull();
        row.NextAttemptAt.Should().BeOnOrAfter(row.LastAttemptAt);
        await repo.Resolve("original", CancellationToken.None);
        await repo.Record(
            "original",
            "123",
            Quarter,
            Quarter,
            HoldingsImportFailureReason.Incomplete,
            CancellationToken.None
        );
        row = await repo.GetAll().AsNoTracking().SingleAsync();
        row.Attempts.Should().Be(2);
        row.ResolvedAt.Should().BeNull();
    }

    [Fact]
    public async Task Resolution_DoesNotClearAConcurrentNewFailure()
    {
        await using var db = fixture.CreateDbContext();
        var failures = new HoldingsImportFailureRepository(db);
        await failures.Record(
            "original",
            "123",
            Quarter,
            Quarter,
            HoldingsImportFailureReason.Incomplete,
            CancellationToken.None
        );
        var first = await failures.GetAll().AsNoTracking().SingleAsync();
        await failures.Record(
            "original",
            "123",
            Quarter,
            Quarter,
            HoldingsImportFailureReason.IdentityConflict,
            CancellationToken.None
        );
        await failures.Resolve("original", CancellationToken.None, first.LastAttemptAt);
        (await failures.GetAll().AsNoTracking().SingleAsync()).ResolvedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recovery_RefusesToOverwriteARetainedAmendmentMissingFromTheSourceResponse(
        bool emptyRestatement
    )
    {
        await using var db = fixture.CreateDbContext();
        var issuer = new Equibles.CommonStocks.Data.Models.EquityIssuer { Name = "Issuer" };
        var holder = new InstitutionalHolder { Cik = "123", Name = "Manager" };
        db.AddRange(issuer, holder);
        if (emptyRestatement)
            db.Add(
                new InstitutionalFiling
                {
                    InstitutionalHolderId = holder.Id,
                    ReportDate = Quarter,
                    FilingDate = new(2026, 9, 1),
                    AccessionNumber = "retained-amendment",
                    FilingType = FilingType.Form13F,
                    IsAmendment = true,
                    PositionCount = 0,
                    DeclaredPositionCount = 1,
                    DeclaredTotalValue = 0,
                }
            );
        else
            db.Add(
                new InstitutionalHolding
                {
                    EquityIssuerId = issuer.Id,
                    InstitutionalHolderId = holder.Id,
                    ReportDate = Quarter,
                    FilingDate = new(2026, 9, 1),
                    FilingType = FilingType.Form13F,
                    AccessionNumber = "retained-amendment",
                    Shares = 17,
                }
            );
        await db.SaveChangesAsync();
        var failures = new HoldingsImportFailureRepository(db);
        await failures.Record(
            "original",
            "123",
            new DateOnly(2026, 8, 1),
            Quarter,
            HoldingsImportFailureReason.Incomplete,
            CancellationToken.None
        );
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetCompanyFilings("123", null, new DateOnly(2026, 8, 1), Arg.Any<DateOnly?>())
            .Returns(
                new List<FilingData>
                {
                    new()
                    {
                        AccessionNumber = "original",
                        Form = "13F-HR",
                        FilingDate = new(2026, 8, 1),
                        ReportDate = Quarter,
                    },
                }
            );
        var ingestion = Substitute.For<Realtime13FIngestionService>(
            edgar,
            new Filing13FXmlParser(),
            new Realtime13FArchiveBuilder(),
            null,
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<Realtime13FIngestionService>>()
        );
        var recovery = new HoldingsImportRecoveryService(
            failures,
            new InstitutionalHoldingRepository(db),
            new RealtimeSweepStateRepository(db),
            edgar,
            ingestion,
            Substitute.For<ILogger<HoldingsImportRecoveryService>>()
        );
        await recovery.Recover(new DateOnly(2020, 1, 1), CancellationToken.None);
        await ingestion
            .DidNotReceive()
            .ReplayRecoveryTail(
                Arg.Any<IReadOnlyCollection<EdgarDailyIndexEntry>>(),
                Arg.Any<IReadOnlySet<string>>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            );
        (await failures.GetAll().AsNoTracking().SingleAsync()).ResolvedAt.Should().BeNull();
        if (emptyRestatement)
            (await db.Set<InstitutionalFiling>().SingleAsync())
                .AccessionNumber.Should()
                .Be("retained-amendment");
        else
            (await db.Set<InstitutionalHolding>().SingleAsync()).Shares.Should().Be(17);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Recovery_ReplaysLaterAmendments_AndOnlyResolvesAfterWholeTail(bool complete)
    {
        await using var db = fixture.CreateDbContext();
        var failures = new HoldingsImportFailureRepository(db);
        await failures.Record(
            "original",
            "123",
            new DateOnly(2026, 8, 1),
            Quarter,
            HoldingsImportFailureReason.Incomplete,
            CancellationToken.None
        );
        await failures
            .GetAll()
            .ExecuteUpdateAsync(s =>
                s.SetProperty(r => r.NextAttemptAt, DateTime.UtcNow.AddDays(-1))
            );
        db.Set<ProcessedFiling>().Add(new ProcessedFiling { AccessionNumber = "amendment" });
        await db.SaveChangesAsync();
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetCompanyFilings("123", null, new DateOnly(2026, 8, 1), Arg.Any<DateOnly?>())
            .Returns(
                new List<FilingData>
                {
                    new()
                    {
                        AccessionNumber = "original",
                        Form = "13F-HR",
                        FilingDate = new(2026, 8, 1),
                        ReportDate = Quarter,
                    },
                    new()
                    {
                        AccessionNumber = "amendment",
                        Form = "13F-HR/A",
                        FilingDate = new(2026, 9, 1),
                        ReportDate = Quarter,
                    },
                }
            );
        var ingestion = Substitute.For<Realtime13FIngestionService>(
            edgar,
            new Filing13FXmlParser(),
            new Realtime13FArchiveBuilder(),
            null,
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<Realtime13FIngestionService>>()
        );
        ingestion
            .ReplayRecoveryTail(
                Arg.Any<IReadOnlyCollection<EdgarDailyIndexEntry>>(),
                Arg.Any<IReadOnlySet<string>>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                complete
                    ? new RecoveryReplayResult(2, 2, 0, ["original"], Complete: true)
                    : new RecoveryReplayResult(2, 1, 0, [], Complete: false)
            );
        var recovery = new HoldingsImportRecoveryService(
            failures,
            new InstitutionalHoldingRepository(db),
            new RealtimeSweepStateRepository(db),
            edgar,
            ingestion,
            Substitute.For<ILogger<HoldingsImportRecoveryService>>()
        );
        await recovery.Recover(new DateOnly(2020, 1, 1), CancellationToken.None);
        await ingestion
            .Received(1)
            .ReplayRecoveryTail(
                Arg.Is<IReadOnlyCollection<EdgarDailyIndexEntry>>(entries =>
                    entries.Count == 2 && entries.Any(e => e.AccessionNumber == "amendment")
                ),
                Arg.Is<IReadOnlySet<string>>(set => set.SetEquals(new[] { "original" })),
                new DateOnly(2020, 1, 1),
                Arg.Any<CancellationToken>()
            );
        var row = await failures.GetAll().AsNoTracking().SingleAsync();
        (row.ResolvedAt != null).Should().Be(complete);
    }

    [Fact]
    public async Task Recovery_ResolvesOnlyThePendingFilingsWhoseLaterTailImported()
    {
        await using var db = fixture.CreateDbContext();
        var failures = new HoldingsImportFailureRepository(db);
        foreach (var (accession, filed) in new[] { ("stuck", 8), ("new", 9) })
            await failures.Record(
                accession,
                "123",
                new DateOnly(2026, filed, 1),
                Quarter,
                HoldingsImportFailureReason.Incomplete,
                CancellationToken.None
            );
        await failures
            .GetAll()
            .ExecuteUpdateAsync(s =>
                s.SetProperty(r => r.NextAttemptAt, DateTime.UtcNow.AddDays(-1))
            );
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetCompanyFilings("123", null, new DateOnly(2026, 8, 1), Arg.Any<DateOnly?>())
            .Returns(
                new List<FilingData>
                {
                    new()
                    {
                        AccessionNumber = "stuck",
                        Form = "13F-HR",
                        FilingDate = new(2026, 8, 1),
                        ReportDate = Quarter,
                    },
                    new()
                    {
                        AccessionNumber = "new",
                        Form = "13F-HR/A",
                        FilingDate = new(2026, 9, 1),
                        ReportDate = Quarter,
                    },
                }
            );
        var ingestion = Substitute.For<Realtime13FIngestionService>(
            edgar,
            new Filing13FXmlParser(),
            new Realtime13FArchiveBuilder(),
            null,
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<Realtime13FIngestionService>>()
        );
        ingestion
            .ReplayRecoveryTail(
                Arg.Any<IReadOnlyCollection<EdgarDailyIndexEntry>>(),
                Arg.Any<IReadOnlySet<string>>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new RecoveryReplayResult(2, 1, 0, ["new"], Complete: false));
        var recovery = new HoldingsImportRecoveryService(
            failures,
            new InstitutionalHoldingRepository(db),
            new RealtimeSweepStateRepository(db),
            edgar,
            ingestion,
            Substitute.For<ILogger<HoldingsImportRecoveryService>>()
        );

        await recovery.Recover(new DateOnly(2020, 1, 1), CancellationToken.None);

        var rows = await failures.GetAll().AsNoTracking().ToDictionaryAsync(r => r.AccessionNumber);
        rows["new"].ResolvedAt.Should().NotBeNull();
        rows["stuck"].ResolvedAt.Should().BeNull("the older filing still failed");
    }

    // directory: gone (EDGAR removed the filing, resolve it as withdrawn) | published (the index
    // only lags, refuse) | unreachable (refuse, nothing proven). A removed filing whose rows are
    // still published is refused too, under its own message.
    [Theory]
    [InlineData("gone", true, false)]
    [InlineData("gone", false, false)]
    [InlineData("gone", true, true)]
    [InlineData("published", true, false)]
    [InlineData("unreachable", true, false)]
    public async Task Recovery_ResolvesAPendingFilingEdgarRemoved_AndRefusesOneItStillPublishes(
        string directory,
        bool otherPending,
        bool withdrawnWroteRows
    )
    {
        await using var db = fixture.CreateDbContext();
        if (withdrawnWroteRows)
        {
            var issuer = new Equibles.CommonStocks.Data.Models.EquityIssuer { Name = "Issuer" };
            var holder = new InstitutionalHolder { Cik = "123", Name = "Manager" };
            db.AddRange(issuer, holder);
            db.Add(
                new InstitutionalHolding
                {
                    EquityIssuerId = issuer.Id,
                    InstitutionalHolderId = holder.Id,
                    ReportDate = Quarter,
                    FilingDate = new(2026, 8, 1),
                    FilingType = FilingType.Form13F,
                    AccessionNumber = "withdrawn",
                    Shares = 17,
                }
            );
            await db.SaveChangesAsync();
        }
        var failures = new HoldingsImportFailureRepository(db);
        await failures.Record(
            "withdrawn",
            "123",
            new DateOnly(2026, 8, 1),
            Quarter,
            HoldingsImportFailureReason.UnreadableSource,
            CancellationToken.None
        );
        if (otherPending)
            await failures.Record(
                "original",
                "123",
                new DateOnly(2026, 8, 2),
                Quarter,
                HoldingsImportFailureReason.Incomplete,
                CancellationToken.None
            );
        await failures
            .GetAll()
            .ExecuteUpdateAsync(s =>
                s.SetProperty(r => r.NextAttemptAt, DateTime.UtcNow.AddDays(-1))
            );
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetCompanyFilings("123", null, new DateOnly(2026, 8, 1), Arg.Any<DateOnly?>())
            .Returns(
                otherPending
                    ? new List<FilingData>
                    {
                        new()
                        {
                            AccessionNumber = "original",
                            Form = "13F-HR",
                            FilingDate = new(2026, 8, 2),
                            ReportDate = Quarter,
                        },
                    }
                    : []
            );
        if (directory == "unreachable")
            edgar
                .FilingExists("123", "withdrawn", Arg.Any<CancellationToken>())
                .Returns<bool>(_ => throw new HttpRequestException("403 Forbidden"));
        else
            edgar
                .FilingExists("123", "withdrawn", Arg.Any<CancellationToken>())
                .Returns(directory == "published");
        var ingestion = Substitute.For<Realtime13FIngestionService>(
            edgar,
            new Filing13FXmlParser(),
            new Realtime13FArchiveBuilder(),
            null,
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<Realtime13FIngestionService>>()
        );
        ingestion
            .ReplayRecoveryTail(
                Arg.Any<IReadOnlyCollection<EdgarDailyIndexEntry>>(),
                Arg.Any<IReadOnlySet<string>>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new RecoveryReplayResult(1, 1, 0, ["original"], Complete: true));
        var recovery = new HoldingsImportRecoveryService(
            failures,
            new InstitutionalHoldingRepository(db),
            new RealtimeSweepStateRepository(db),
            edgar,
            ingestion,
            Substitute.For<ILogger<HoldingsImportRecoveryService>>()
        );

        await recovery.Recover(new DateOnly(2020, 1, 1), CancellationToken.None);

        var rows = await failures.GetAll().AsNoTracking().ToDictionaryAsync(r => r.AccessionNumber);
        var gone = directory == "gone" && !withdrawnWroteRows;
        await ingestion
            .Received(gone && otherPending ? 1 : 0)
            .ReplayRecoveryTail(
                Arg.Any<IReadOnlyCollection<EdgarDailyIndexEntry>>(),
                Arg.Is<IReadOnlySet<string>>(set => set.SetEquals(new[] { "original" })),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            );
        (rows["withdrawn"].ResolvedAt != null).Should().Be(gone);
        rows["withdrawn"]
            .Reason.Should()
            .Be(
                gone
                    ? HoldingsImportFailureReason.SourceWithdrawn
                    : HoldingsImportFailureReason.UnreadableSource
            );
        if (otherPending)
        {
            (rows["original"].ResolvedAt != null).Should().Be(gone);
            rows["original"].Reason.Should().Be(HoldingsImportFailureReason.Incomplete);
        }
        if (withdrawnWroteRows)
        {
            await edgar
                .DidNotReceive()
                .FilingExists(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            (await db.Set<InstitutionalHolding>().SingleAsync()).Shares.Should().Be(17);
        }
    }

    // A resolved row that keeps its failure reason means the filing's holdings were written; every
    // row closed without a write names the source proof instead.
    [Fact]
    public async Task Recovery_NamesTheSourceProofOfEveryRowItClosesWithoutAWrite()
    {
        await using var db = fixture.CreateDbContext();
        var failures = new HoldingsImportFailureRepository(db);
        var filed = new DateOnly(2026, 8, 1);
        foreach (var accession in new[] { "old", "written", "placeholder", "withheld" })
            await failures.Record(
                accession,
                "123",
                filed,
                accession == "old" ? new DateOnly(2019, 3, 31) : Quarter,
                HoldingsImportFailureReason.Incomplete,
                CancellationToken.None
            );
        await failures
            .GetAll()
            .ExecuteUpdateAsync(s =>
                s.SetProperty(r => r.NextAttemptAt, DateTime.UtcNow.AddDays(-1))
            );
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetCompanyFilings("123", null, filed, Arg.Any<DateOnly?>())
            .Returns(
                new[] { "old", "written", "placeholder", "withheld" }
                    .Select(accession => new FilingData
                    {
                        AccessionNumber = accession,
                        Form = "13F-HR",
                        FilingDate = filed,
                        ReportDate = accession == "old" ? new DateOnly(2019, 3, 31) : Quarter,
                    })
                    .ToList()
            );
        var ingestion = Substitute.For<Realtime13FIngestionService>(
            edgar,
            new Filing13FXmlParser(),
            new Realtime13FArchiveBuilder(),
            null,
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<Realtime13FIngestionService>>()
        );
        ingestion
            .ReplayRecoveryTail(
                Arg.Any<IReadOnlyCollection<EdgarDailyIndexEntry>>(),
                Arg.Any<IReadOnlySet<string>>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new RecoveryReplayResult(3, 3, 0, ["written", "placeholder", "withheld"], true)
                {
                    ClosedWithoutRows = new Dictionary<string, HoldingsImportFailureReason>
                    {
                        ["placeholder"] = HoldingsImportFailureReason.NoTrackedPositions,
                        ["withheld"] = HoldingsImportFailureReason.ConfidentialTreatment,
                    },
                }
            );
        var recovery = new HoldingsImportRecoveryService(
            failures,
            new InstitutionalHoldingRepository(db),
            new RealtimeSweepStateRepository(db),
            edgar,
            ingestion,
            Substitute.For<ILogger<HoldingsImportRecoveryService>>()
        );

        await recovery.Recover(new DateOnly(2020, 1, 1), CancellationToken.None);

        var rows = await failures.GetAll().AsNoTracking().ToDictionaryAsync(r => r.AccessionNumber);
        rows.Values.Should().OnlyContain(row => row.ResolvedAt != null);
        rows["old"].Reason.Should().Be(HoldingsImportFailureReason.OutsideHistoryWindow);
        rows["written"].Reason.Should().Be(HoldingsImportFailureReason.Incomplete);
        rows["placeholder"].Reason.Should().Be(HoldingsImportFailureReason.NoTrackedPositions);
        rows["withheld"].Reason.Should().Be(HoldingsImportFailureReason.ConfidentialTreatment);
    }

    [Fact]
    public async Task Coverage_ReportsFailuresEvenWhenArchiveIsProcessed()
    {
        await using var db = fixture.CreateDbContext();
        db.Add(
            new ProcessedDataSet
            {
                FileName = "01jun2026-31aug2026_form13f.zip",
                ParserVersion = ProcessedDataSet.CurrentParserVersion,
            }
        );
        db.Add(
            new RealtimeSweepState
            {
                WorkerName = "Holdings13FRealtime",
                SweptThrough = DateOnly.FromDateTime(DateTime.UtcNow),
            }
        );
        await db.SaveChangesAsync();
        var failures = new HoldingsImportFailureRepository(db);
        await failures.Record(
            "original",
            "123",
            Quarter,
            Quarter,
            HoldingsImportFailureReason.IdentityConflict,
            CancellationToken.None
        );
        var coverage = new HoldingsImportCoverage(
            failures,
            new ProcessedDataSetRepository(db),
            new RealtimeSweepStateRepository(db),
            new HoldingsCusipRescanRepository(db)
        );
        (await coverage.GetIncompleteReason(Quarter)).Should().Contain("not finished importing");
        await failures.Resolve("original", CancellationToken.None);
        (await coverage.GetIncompleteReason(Quarter)).Should().BeNull();
    }

    [Fact]
    public async Task Coverage_CountsAnUndatedFailureOnlyForQuartersEndingBeforeItsFiling()
    {
        await using var db = fixture.CreateDbContext();
        db.Add(
            new RealtimeSweepState
            {
                WorkerName = "Holdings13FRealtime",
                SweptThrough = DateOnly.FromDateTime(DateTime.UtcNow),
            }
        );
        await db.SaveChangesAsync();
        var failures = new HoldingsImportFailureRepository(db);
        await failures.Record(
            "undated",
            "123",
            Quarter.AddDays(30),
            null,
            HoldingsImportFailureReason.UnreadableSource,
            CancellationToken.None
        );
        var coverage = new HoldingsImportCoverage(
            failures,
            new ProcessedDataSetRepository(db),
            new RealtimeSweepStateRepository(db),
            new HoldingsCusipRescanRepository(db)
        );

        (await coverage.GetIncompleteReason(Quarter)).Should().Contain("not finished importing");
        (await coverage.GetIncompleteReason(new DateOnly(2026, 9, 30))).Should().BeNull();
        (await coverage.GetIncompleteReason(default)).Should().Contain("not finished importing");
        await failures.Record(
            "undated-unfiled",
            "456",
            default,
            null,
            HoldingsImportFailureReason.UnreadableSource,
            CancellationToken.None
        );
        (await coverage.GetIncompleteReason(new DateOnly(2026, 9, 30)))
            .Should()
            .Contain("not finished importing");
    }

    [Fact]
    public async Task Recovery_TakesAFilingNotYetStoredBeforeTheStoredReplays()
    {
        await using var db = fixture.CreateDbContext();
        var failures = new HoldingsImportFailureRepository(db);
        for (var i = 0; i < 12; i++)
            await failures.EnqueueRecovery(
                $"stored-{i}",
                $"{100 + i}",
                new DateOnly(2026, 8, 1),
                CancellationToken.None,
                supersedesActiveAttempt: true,
                reason: HoldingsImportFailureReason.StoredReplay
            );
        // Queued last, so the oldest-first order alone would leave it behind twelve replays.
        await failures.EnqueueRecovery(
            "filed-today",
            "999",
            new DateOnly(2026, 11, 16),
            CancellationToken.None
        );
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetCompanyFilings(Arg.Any<string>(), null, Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>())
            .Returns([]);
        edgar
            .FilingExists(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var recovery = new HoldingsImportRecoveryService(
            failures,
            new InstitutionalHoldingRepository(db),
            new RealtimeSweepStateRepository(db),
            edgar,
            Substitute.For<Realtime13FIngestionService>(
                edgar,
                new Filing13FXmlParser(),
                new Realtime13FArchiveBuilder(),
                null,
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<Realtime13FIngestionService>>()
            ),
            Substitute.For<ILogger<HoldingsImportRecoveryService>>()
        );

        await recovery.Recover(new DateOnly(2020, 1, 1), CancellationToken.None);

        await edgar
            .Received(1)
            .GetCompanyFilings("999", null, Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>());
        (
            await failures
                .GetAll()
                .CountAsync(row =>
                    row.Reason == HoldingsImportFailureReason.StoredReplay
                    && row.NextAttemptAt <= DateTime.UtcNow
                )
        )
            .Should()
            .Be(3);
    }

    [Fact]
    public async Task Recovery_StartsTheTailAtARewoundSweepCursor()
    {
        var rewound = new DateOnly(2026, 9, 1);
        var filedToday = new DateOnly(2026, 11, 16);
        await using var db = fixture.CreateDbContext();
        db.Add(
            new RealtimeSweepState { WorkerName = "Holdings13FRealtime", SweptThrough = rewound }
        );
        await db.SaveChangesAsync();
        var failures = new HoldingsImportFailureRepository(db);
        await failures.EnqueueRecovery("restatement", "123", filedToday, CancellationToken.None);
        var edgar = Substitute.For<ISecEdgarClient>();
        // The original's ledger row went with the rewind, so the sweep still owes its re-import.
        edgar
            .GetCompanyFilings("123", null, rewound, Arg.Any<DateOnly?>())
            .Returns(
                new List<FilingData>
                {
                    new()
                    {
                        AccessionNumber = "original",
                        Form = "13F-HR",
                        FilingDate = new(2026, 11, 10),
                        ReportDate = new(2026, 9, 30),
                    },
                    new()
                    {
                        AccessionNumber = "restatement",
                        Form = "13F-HR/A",
                        FilingDate = filedToday,
                        ReportDate = new(2026, 9, 30),
                    },
                }
            );
        var ingestion = Substitute.For<Realtime13FIngestionService>(
            edgar,
            new Filing13FXmlParser(),
            new Realtime13FArchiveBuilder(),
            null,
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<Realtime13FIngestionService>>()
        );
        ingestion
            .ReplayRecoveryTail(
                Arg.Any<IReadOnlyCollection<EdgarDailyIndexEntry>>(),
                Arg.Any<IReadOnlySet<string>>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new RecoveryReplayResult(2, 2, 0, ["restatement"], Complete: true));
        var recovery = new HoldingsImportRecoveryService(
            failures,
            new InstitutionalHoldingRepository(db),
            new RealtimeSweepStateRepository(db),
            edgar,
            ingestion,
            Substitute.For<ILogger<HoldingsImportRecoveryService>>()
        );

        await recovery.Recover(new DateOnly(2020, 1, 1), CancellationToken.None);

        await ingestion
            .Received(1)
            .ReplayRecoveryTail(
                Arg.Is<IReadOnlyCollection<EdgarDailyIndexEntry>>(entries =>
                    entries
                        .Select(e => e.AccessionNumber)
                        .SequenceEqual(new[] { "original", "restatement" })
                ),
                Arg.Is<IReadOnlySet<string>>(set => set.SetEquals(new[] { "restatement" })),
                new DateOnly(2020, 1, 1),
                Arg.Any<CancellationToken>()
            );
        (await failures.GetAll().AsNoTracking().SingleAsync()).ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Coverage_LeavesOutStoredFilingsAwaitingReplay()
    {
        await using var db = fixture.CreateDbContext();
        db.Add(
            new RealtimeSweepState
            {
                WorkerName = HoldingsImportCoverage.SweepWorkerName,
                SweptThrough = DateOnly.FromDateTime(DateTime.UtcNow),
            }
        );
        await db.SaveChangesAsync();
        var failures = new HoldingsImportFailureRepository(db);
        await failures.EnqueueRecovery(
            "stored",
            "123",
            Quarter.AddDays(30),
            CancellationToken.None,
            supersedesActiveAttempt: true,
            reason: HoldingsImportFailureReason.StoredReplay
        );
        var coverage = new HoldingsImportCoverage(
            failures,
            new ProcessedDataSetRepository(db),
            new RealtimeSweepStateRepository(db),
            new HoldingsCusipRescanRepository(db)
        );

        (await coverage.GetIncompleteReason(Quarter)).Should().BeNull();

        await failures.EnqueueRecovery(
            "unstored",
            "456",
            Quarter.AddDays(30),
            CancellationToken.None
        );
        (await coverage.GetIncompleteReason(Quarter)).Should().Contain("not finished importing");
    }

    [Fact]
    public async Task Coverage_ReadsRecentFilingsFromDiscoveryWhileTheSweepIsRewound()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var db = fixture.CreateDbContext();
        db.Add(
            new RealtimeSweepState
            {
                WorkerName = HoldingsImportCoverage.SweepWorkerName,
                SweptThrough = today.AddDays(-60),
            }
        );
        await db.SaveChangesAsync();
        var coverage = new HoldingsImportCoverage(
            new HoldingsImportFailureRepository(db),
            new ProcessedDataSetRepository(db),
            new RealtimeSweepStateRepository(db),
            new HoldingsCusipRescanRepository(db)
        );

        (await coverage.GetIncompleteReason(Quarter)).Should().Contain("Recent SEC filings");

        db.Add(
            new RealtimeSweepState
            {
                WorkerName = HoldingsImportCoverage.DiscoveryWorkerName,
                SweptThrough = today,
            }
        );
        await db.SaveChangesAsync();
        (await coverage.GetIncompleteReason(Quarter)).Should().BeNull();
    }
}
