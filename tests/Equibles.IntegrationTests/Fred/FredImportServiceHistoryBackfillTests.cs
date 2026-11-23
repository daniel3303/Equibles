using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Fred.Data;
using Equibles.Fred.Data.Models;
using Equibles.Fred.HostedService.Configuration;
using Equibles.Fred.HostedService.Services;
using Equibles.Fred.Repositories;
using Equibles.Integrations.Fred.Contracts;
using Equibles.Integrations.Fred.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Fred;

// Stored history started at Worker:MinSyncDate, so CPI had no year-ago base before 2021. Each series is filled back
// once to its own first observation, and a second cycle asks FRED for nothing older.
public class FredImportServiceHistoryBackfillTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly FredSeriesRepository _seriesRepo;
    private readonly FredObservationRepository _obsRepo;
    private readonly IFredClient _fredClient = Substitute.For<IFredClient>();

    public FredImportServiceHistoryBackfillTests()
    {
        _dbContext = TestDbContextFactory.Create(new FredModuleConfiguration());
        _seriesRepo = new FredSeriesRepository(_dbContext);
        _obsRepo = new FredObservationRepository(_dbContext);
        _fredClient
            .GetSeriesMetadata(Arg.Any<string>())
            .Returns(Task.FromResult<FredSeriesRecord>(null));
        _fredClient
            .GetObservations(Arg.Any<string>(), Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>())
            .Returns(_ => Task.FromResult(new List<FredObservationRecord>()));
    }

    public void Dispose() => _dbContext.Dispose();

    private FredImportService Sut(
        DateTime? historyStart = null,
        FredObservationRepository observations = null
    ) =>
        new(
            ServiceScopeSubstitute.Create(
                (typeof(FredSeriesRepository), _seriesRepo),
                (typeof(FredObservationRepository), observations ?? _obsRepo)
            ),
            Substitute.For<ILogger<FredImportService>>(),
            _fredClient,
            Options.Create(new WorkerOptions { MinSyncDate = new DateTime(2020, 1, 1) }),
            Substitute.For<ErrorReporter>(
                Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new FredScraperOptions { HistoryStart = historyStart })
        );

    private async Task<FredSeries> SeedCpi(DateOnly observationStart, params string[] storedMonths)
    {
        var series = new FredSeries
        {
            SeriesId = "CPIAUCSL",
            Title = "Consumer Price Index for All Urban Consumers: All Items in U.S. City Average",
            Category = FredSeriesCategory.Inflation,
            Frequency = "M",
            Units = "Index 1982-1984=100",
            SeasonalAdjustment = "SA",
            ObservationStart = observationStart,
        };
        _dbContext.Set<FredSeries>().Add(series);
        foreach (var month in storedMonths)
            _dbContext
                .Set<FredObservation>()
                .Add(
                    new FredObservation
                    {
                        FredSeriesId = series.Id,
                        Date = DateOnly.Parse(month),
                        Value = 258.687m,
                    }
                );
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        return series;
    }

    private void FredHolds(
        DateOnly start,
        DateOnly end,
        params (string Date, string Value)[] rows
    ) =>
        _fredClient
            .GetObservations("CPIAUCSL", start, end)
            .Returns(
                Task.FromResult(
                    rows.Select(row => new FredObservationRecord
                        {
                            Date = row.Date,
                            Value = row.Value,
                        })
                        .ToList()
                )
            );

    private Task ReceivedHistoryCalls(int count) =>
        _fredClient
            .Received(count)
            .GetObservations(
                "CPIAUCSL",
                Arg.Any<DateOnly?>(),
                Arg.Is<DateOnly?>(end => end != null)
            );

    [Fact]
    public async Task Import_StoredHistoryStartsAfterTheSeries_FillsItBackOnce()
    {
        var series = await SeedCpi(new DateOnly(1947, 1, 1), "2020-01-01", "2020-02-01");
        FredHolds(
            new DateOnly(1947, 1, 1),
            new DateOnly(2019, 12, 31),
            ("1947-01-01", "21.48"),
            ("1947-02-01", "21.62"),
            ("2019-12-01", "258.444")
        );

        await Sut().Import(CancellationToken.None);
        await Sut().Import(CancellationToken.None);

        var dates = await _obsRepo
            .GetBySeries(series)
            .OrderBy(o => o.Date)
            .Select(o => o.Date)
            .ToListAsync();
        dates
            .Should()
            .Equal(
                new DateOnly(1947, 1, 1),
                new DateOnly(1947, 2, 1),
                new DateOnly(2019, 12, 1),
                new DateOnly(2020, 1, 1),
                new DateOnly(2020, 2, 1)
            );
        await ReceivedHistoryCalls(1);
    }

    [Fact]
    public async Task Import_FredServesNothingOlder_TakesTheStoredStartAndStopsAsking()
    {
        // A licensed series keeps a rolling window: FRED no longer serves its recorded start.
        var series = await SeedCpi(new DateOnly(2016, 11, 1), "2020-01-01");

        await Sut().Import(CancellationToken.None);
        await Sut().Import(CancellationToken.None);

        (await _seriesRepo.Get(series.Id)).ObservationStart.Should().Be(new DateOnly(2020, 1, 1));
        await ReceivedHistoryCalls(1);
    }

    [Fact]
    public async Task Import_HistoryStartConfigured_FillsBackOnlyToIt()
    {
        var series = await SeedCpi(new DateOnly(1947, 1, 1), "2020-01-01");

        await Sut(historyStart: new DateTime(2010, 1, 1)).Import(CancellationToken.None);

        await _fredClient
            .Received(1)
            .GetObservations("CPIAUCSL", new DateOnly(2010, 1, 1), new DateOnly(2019, 12, 31));
        // The configured floor is ours, not FRED's: the series keeps its own recorded start.
        (await _seriesRepo.Get(series.Id))
            .ObservationStart.Should()
            .Be(new DateOnly(1947, 1, 1));
    }

    // A worker restart mid-fill: the batches already saved must leave the gap visible, so the next cycle completes it.
    [Fact]
    public async Task Import_FillCutShortAfterOneBatch_NextCycleCompletesIt()
    {
        var start = new DateOnly(2015, 11, 23);
        var series = await SeedCpi(start, "2020-01-01");
        var history = Enumerable.Range(0, 1500).Select(day => start.AddDays(day)).ToList();
        _fredClient
            .GetObservations(
                "CPIAUCSL",
                Arg.Any<DateOnly?>(),
                Arg.Is<DateOnly?>(end => end != null)
            )
            .Returns(call =>
                Task.FromResult(
                    history
                        .Where(date =>
                            date >= call.ArgAt<DateOnly?>(1) && date <= call.ArgAt<DateOnly?>(2)
                        )
                        .Select(date => new FredObservationRecord
                        {
                            Date = $"{date:yyyy-MM-dd}",
                            Value = "250",
                        })
                        .ToList()
                )
            );

        await Sut(observations: new StopsAfterOneBatch(_dbContext)).Import(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();
        await Sut().Import(CancellationToken.None);

        var stored = await _obsRepo.GetBySeries(series).Select(o => o.Date).ToListAsync();
        stored.Should().HaveCount(1501).And.Contain(history);
    }

    private sealed class StopsAfterOneBatch(EquiblesFinancialDbContext dbContext)
        : FredObservationRepository(dbContext)
    {
        private int _batches;

        public override void AddRange(IEnumerable<FredObservation> entities)
        {
            if (++_batches > 1)
                throw new InvalidOperationException("The worker stopped.");
            base.AddRange(entities);
        }
    }

    [Fact]
    public async Task Import_NothingStoredYet_LeavesHistoryToTheNextCycle()
    {
        await SeedCpi(new DateOnly(1947, 1, 1));

        await Sut().Import(CancellationToken.None);

        await ReceivedHistoryCalls(0);
    }
}
