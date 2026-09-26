using System.Collections.Concurrent;
using System.Data.Common;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

[Collection(ParadeDbCollection.Name)]
public class Zero13FRestatementImportTests(ParadeDbFixture fixture) : IAsyncLifetime
{
    private static readonly DateOnly Quarter = new(2026, 6, 30);
    private static readonly DateOnly Previous = new(2026, 3, 31);
    private static readonly EdgarDailyIndexEntry Entry = new()
    {
        Cik = "1729347",
        AccessionNumber = "0001999371-26-020307",
        DateFiled = new DateOnly(2026, 9, 10),
        FormType = "13F-HR/A",
    };
    private readonly ConcurrentBag<EquiblesFinancialDbContext> _contexts = [];

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var context in _contexts)
            context.Dispose();
        return Task.CompletedTask;
    }

    private EquiblesFinancialDbContext Context(
        bool rejectEmptySummary = false,
        DbCommandInterceptor observer = null
    )
    {
        var context = fixture.CreateDbContext(options =>
        {
            if (rejectEmptySummary)
                options.AddInterceptors(new RejectEmptySummary());
            if (observer != null)
                options.AddInterceptors(observer);
        });
        _contexts.Add(context);
        return context;
    }

    private IServiceScopeFactory Scopes(
        bool rejectEmptySummary = false,
        DbCommandInterceptor observer = null
    )
    {
        var factory = Substitute.For<IServiceScopeFactory>();
        factory
            .CreateScope()
            .Returns(_ =>
            {
                var db = Context(rejectEmptySummary, observer);
                var services = Substitute.For<IServiceProvider>();
                services.GetService(typeof(EquiblesFinancialDbContext)).Returns(db);
                services
                    .GetService(typeof(EquityIssuerRepository))
                    .Returns(new EquityIssuerRepository(db));
                services
                    .GetService(typeof(InstitutionalHolderRepository))
                    .Returns(new InstitutionalHolderRepository(db));
                services
                    .GetService(typeof(InstitutionalHoldingRepository))
                    .Returns(new InstitutionalHoldingRepository(db));
                services
                    .GetService(typeof(ProcessedFilingRepository))
                    .Returns(new ProcessedFilingRepository(db));
                services
                    .GetService(typeof(HoldingsImportFailureRepository))
                    .Returns(new HoldingsImportFailureRepository(db));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(services);
                return scope;
            });
        return factory;
    }

    private static byte[] Source() =>
        File.ReadAllBytes(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Holdings",
                "13f-zero-restatement-submission.txt"
            )
        );

    private HoldingsImportService Importer(IServiceScopeFactory scopes)
    {
        var prices = Substitute.For<IStockPriceProvider>();
        prices
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, string, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(new Dictionary<(Guid, string, DateOnly), decimal>()));
        return new(
            scopes,
            NullLogger<HoldingsImportService>.Instance,
            Options.Create(new WorkerOptions()),
            prices,
            Substitute.For<IBus>()
        );
    }

    [Fact]
    public async Task CompleteRestatement_ReplacesOnlyIts13FBookAndDoesNotCarryEarlierPositions()
    {
        var stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "ZERO",
            Name: "Example equity",
            Cik: "12345",
            Cusip: "123456789"
        );
        var holder = new InstitutionalHolder { Cik = Entry.Cik, Name = "Restating manager" };
        var other = new InstitutionalHolder { Cik = "999999", Name = "Other manager" };
        var removed = Position(stock, holder, Quarter, FilingType.Form13F, "ORIGINAL");
        var retained = new[]
        {
            Position(stock, holder, Previous, FilingType.Form13F, "PRIOR"),
            Position(stock, holder, Quarter, FilingType.Schedule13G, "SCHEDULE"),
            Position(stock, other, Quarter, FilingType.Form13F, "OTHER"),
        };
        using (var seed = Context())
        {
            seed.Add(stock);
            seed.AddRange(holder, other);
            seed.Add(removed);
            seed.AddRange(retained);
            seed.Add(
                new InstitutionalFiling
                {
                    AccessionNumber = "ORIGINAL",
                    InstitutionalHolderId = holder.Id,
                    ReportDate = Quarter,
                    FilingDate = new DateOnly(2026, 8, 13),
                    PositionCount = 1,
                    TotalValue = 500,
                }
            );
            await seed.SaveChangesAsync();
        }
        var scopes = Scopes();
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetDocumentContent(Entry.AccessionNumber, Entry.Cik, Arg.Any<CancellationToken>())
            .Returns(System.Text.Encoding.UTF8.GetString(Source()));
        var service = new Realtime13FIngestionService(
            edgar,
            new Filing13FXmlParser(),
            new Realtime13FArchiveBuilder(),
            Importer(scopes),
            scopes,
            NullLogger<Realtime13FIngestionService>.Instance
        );

        for (var attempt = 0; attempt < 2; attempt++)
            (await service.IngestSpecificFilings([Entry], Previous, CancellationToken.None))
                .Should()
                .Be(1);

        using var verify = Context();
        var stored = await verify
            .Set<InstitutionalHolding>()
            .Include(h => h.ManagerEntries)
            .ToListAsync();
        stored.Select(h => h.Id).Should().BeEquivalentTo(retained.Select(h => h.Id));
        stored.Should().OnlyContain(h => h.Shares == 10 && h.ManagerEntries.Count == 1);
        var filing = await verify
            .Set<InstitutionalFiling>()
            .SingleAsync(f => f.AccessionNumber == Entry.AccessionNumber);
        filing.PositionCount.Should().Be(0);
        filing.TotalValue.Should().Be(0);
        filing.DeclaredPositionCount.Should().Be(1);
        filing.DeclaredTotalValue.Should().Be(0);
        (await verify.Set<InstitutionalFiling>().AnyAsync(f => f.AccessionNumber == "ORIGINAL"))
            .Should()
            .BeFalse();
        (await verify.Set<AumQuarterlySnapshot>().SingleAsync(s => s.ReportDate == Quarter))
            .DirtyAt.Should()
            .NotBeNull();
        var repository = new InstitutionalHoldingRepository(verify);
        (await repository.GetCombinedQuarterByStock(stock, Quarter, Previous).ToListAsync())
            .Should()
            .OnlyContain(h => h.InstitutionalHolderId == other.Id);
        (await repository.GetCombinedQuarter(Quarter, Previous).ToListAsync())
            .Should()
            .OnlyContain(h => h.InstitutionalHolderId == other.Id);
        (await repository.GetFiledHolderIdsAmong(Quarter, [holder.Id]).ToListAsync())
            .Should()
            .ContainSingle();
        (await repository.Get13FReportDatesByHolder(holder).ToListAsync())
            .Should()
            .Equal(Quarter, Previous);
        (await repository.Get13FReportDatesByHolderSnapshotBacked(holder))
            .Should()
            .Equal(Quarter, Previous);
        var activity = await repository
            .GetQuarterlyActivityCombined(Quarter, Previous)
            .SingleAsync();
        activity.CurrentShares.Should().Be(10);
        activity.PreviousShares.Should().Be(10);
        (await repository.GetQuarterlyNewSoldOutPositionsCombined(Quarter, Previous).SingleAsync())
            .SoldOutFilerCount.Should()
            .Be(1);

        await new HoldingsAggregateRefreshService(
            scopes,
            NullLogger<HoldingsAggregateRefreshService>.Instance
        ).RebuildQuarterAsync(Quarter, CancellationToken.None);
        using var snapshots = Context();
        var empty = await snapshots
            .Set<HolderQuarterlySnapshot>()
            .SingleAsync(s => s.InstitutionalHolderId == holder.Id && s.ReportDate == Quarter);
        empty.PositionCount.Should().Be(0);
        empty.Aum.Should().Be(0);
        (await snapshots.Set<AumQuarterlySnapshot>().SingleAsync(s => s.ReportDate == Quarter))
            .FilerCount.Should()
            .Be(2);

        using var addition = new Realtime13FArchiveBuilder().Build([
            new Parsed13FFiling
            {
                Cik = Entry.Cik,
                AccessionNumber = "LATER-ADDITION",
                FilingDate = Entry.DateFiled.AddDays(1),
                PeriodOfReport = Quarter,
                IsAmendment = true,
                AmendmentType = "NEW HOLDINGS",
                FilingManagerName = holder.Name,
                Holdings =
                [
                    new Parsed13FHolding
                    {
                        Cusip = "123456789",
                        Shares = 20,
                        ShareType = "SH",
                        Value = 1000,
                        InvestmentDiscretion = "SOLE",
                    },
                ],
            },
        ]);
        (await Importer(scopes).ImportDataSet(addition, Previous, CancellationToken.None))
            .IsComplete.Should()
            .BeTrue();
        using var afterAddition = Context();
        (
            await afterAddition
                .Set<InstitutionalHolding>()
                .SingleAsync(h =>
                    h.InstitutionalHolderId == holder.Id
                    && h.ReportDate == Quarter
                    && h.FilingType == FilingType.Form13F
                )
        )
            .Shares.Should()
            .Be(20);
        (
            await afterAddition
                .Set<InstitutionalFiling>()
                .AnyAsync(f => f.AccessionNumber == Entry.AccessionNumber)
        )
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task FailedSummaryWrite_RollsBackPositionAndManagerDeletion()
    {
        var filing = Filing13FSubmissionParser
            .Parse(System.Text.Encoding.UTF8.GetString(Source()), Entry, new())
            .Filing;
        var stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "ZERO",
            Name: "Example equity",
            Cik: "12345",
            Cusip: "123456789"
        );
        var holder = new InstitutionalHolder { Cik = Entry.Cik, Name = "Restating manager" };
        var original = Position(stock, holder, Quarter, FilingType.Form13F, "ORIGINAL");
        using (var seed = Context())
        {
            seed.Add(stock);
            seed.Add(holder);
            seed.Add(original);
            await seed.SaveChangesAsync();
        }
        var action = () =>
            Importer(Scopes(rejectEmptySummary: true))
                .ImportZeroRestatement(filing, Previous, CancellationToken.None);
        await action
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Injected summary failure");
        using var verify = Context();
        var retained = await verify
            .Set<InstitutionalHolding>()
            .Include(h => h.ManagerEntries)
            .SingleAsync();
        retained.Id.Should().Be(original.Id);
        retained.Shares.Should().Be(10);
        retained.ManagerEntries.Should().ContainSingle();
        (await verify.Set<InstitutionalFiling>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentSummaryRead_HoldsTheBookLockUntilItsWritesFinish()
    {
        var stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "ZERO",
            Name: "Example equity",
            Cik: "12345",
            Cusip: "123456789"
        );
        var holder = new InstitutionalHolder { Cik = Entry.Cik, Name = "Restating manager" };
        using (var seed = Context())
        {
            seed.Add(stock);
            seed.Add(holder);
            await seed.SaveChangesAsync();
        }
        using var original = new Realtime13FArchiveBuilder().Build([
            new Parsed13FFiling
            {
                Cik = Entry.Cik,
                AccessionNumber = "ORIGINAL",
                FilingDate = Entry.DateFiled.AddDays(-1),
                PeriodOfReport = Quarter,
                FilingManagerName = holder.Name,
                Holdings =
                [
                    new Parsed13FHolding
                    {
                        Cusip = "123456789",
                        Shares = 10,
                        Value = 500,
                        ShareType = "SH",
                    },
                ],
            },
        ]);
        var pause = new SummaryReadPause();
        var originalTask = Importer(Scopes(observer: pause))
            .ImportDataSet(original, Previous, CancellationToken.None);
        Task<ImportResult> zeroTask = null;
        try
        {
            await pause.Read.Task.WaitAsync(TimeSpan.FromSeconds(15));
            using var probe = Context();
            var key = $"holdings-book:{holder.Id}:2026-06-30:0";
            (
                await probe
                    .Database.SqlQuery<bool>(
                        $"SELECT pg_try_advisory_xact_lock(hashtextextended({key}, 0)) AS \"Value\""
                    )
                    .SingleAsync()
            )
                .Should()
                .BeFalse("summary reads and writes must share the position writer's lock");
            var filing = Filing13FSubmissionParser
                .Parse(System.Text.Encoding.UTF8.GetString(Source()), Entry, new())
                .Filing;
            zeroTask = Importer(Scopes())
                .ImportZeroRestatement(filing, Previous, CancellationToken.None);
        }
        finally
        {
            pause.Resume.TrySetResult();
            await originalTask;
            if (zeroTask != null)
                await zeroTask;
        }
        using var verify = Context();
        (await verify.Set<InstitutionalHolding>().AnyAsync()).Should().BeFalse();
        (await verify.Set<InstitutionalFiling>().SingleAsync())
            .AccessionNumber.Should()
            .Be(Entry.AccessionNumber);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyQuarter_RemainsSelectableAndDoesNotDoubleCountAnInFlightPositiveBook(
        bool positiveBook
    )
    {
        var stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "ZERO",
            Name: "Example equity",
            Cik: "12345",
            Cusip: "123456789"
        );
        var holder = new InstitutionalHolder { Cik = Entry.Cik, Name = "Restating manager" };
        using (var seed = Context())
        {
            seed.Add(stock);
            seed.Add(holder);
            seed.Add(Position(stock, holder, Previous, FilingType.Form13F, "PRIOR"));
            await seed.SaveChangesAsync();
        }
        using var read = Context();
        var repository = new InstitutionalHoldingRepository(read);
        (await repository.Get13FAvailableReportDatesCached()).Should().Equal(Previous);
        using (var seed = Context())
        {
            seed.Add(
                new InstitutionalFiling
                {
                    InstitutionalHolderId = holder.Id,
                    ReportDate = Quarter,
                    FilingDate = Entry.DateFiled,
                    AccessionNumber = Entry.AccessionNumber,
                    IsAmendment = true,
                    FilingType = FilingType.Form13F,
                    PositionCount = 0,
                    DeclaredPositionCount = 1,
                    DeclaredTotalValue = 0,
                }
            );
            if (positiveBook)
                seed.Add(Position(stock, holder, Quarter, FilingType.Form13F, "LATER"));
            await seed.SaveChangesAsync();
        }
        (await repository.Get13FAvailableReportDates().ToListAsync())
            .Should()
            .Equal(Quarter, Previous);
        (await repository.Get13FAvailableReportDatesCached()).Should().Equal(Quarter, Previous);
        await new HoldingsAggregateRefreshService(
            Scopes(),
            NullLogger<HoldingsAggregateRefreshService>.Instance
        ).RebuildQuarterAsync(Quarter, CancellationToken.None);
        using var verify = Context();
        var snapshot = await verify
            .Set<AumQuarterlySnapshot>()
            .SingleAsync(s => s.ReportDate == Quarter);
        snapshot.FilerCount.Should().Be(1);
        snapshot.TotalValue.Should().Be(positiveBook ? 500 : 0);
        snapshot.PositionCount.Should().Be(positiveBook ? 1 : 0);
        (await verify.Set<HolderQuarterlySnapshot>().SingleAsync(s => s.ReportDate == Quarter))
            .PositionCount.Should()
            .Be(positiveBook ? 1 : 0);
    }

    private sealed class SummaryReadPause : DbCommandInterceptor
    {
        public TaskCompletionSource Read { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _paused;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                command.CommandText.Contains("InstitutionalHolding")
                && command.CommandText.Contains("GROUP BY")
                && Interlocked.Exchange(ref _paused, 1) == 0
            )
            {
                Read.TrySetResult();
                await Resume.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }

    private sealed class RejectEmptySummary : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                eventData
                    .Context.ChangeTracker.Entries<InstitutionalFiling>()
                    .Any(e => e.State == EntityState.Added && e.Entity.PositionCount == 0)
            )
                throw new InvalidOperationException("Injected summary failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static InstitutionalHolding Position(
        EquityIssuer stock,
        InstitutionalHolder holder,
        DateOnly date,
        FilingType type,
        string accession
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            InstitutionalHolderId = holder.Id,
            ReportDate = date,
            FilingDate = date.AddDays(40),
            FilingType = type,
            AccessionNumber = accession,
            Cusip = "123456789",
            Shares = 10,
            Value = 500,
            ManagerEntries = [new HoldingManagerEntry { Shares = 10, Value = 500 }],
        };
}
