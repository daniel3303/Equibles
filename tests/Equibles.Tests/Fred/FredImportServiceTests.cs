using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Fred.Data;
using Equibles.Fred.Data.Models;
using Equibles.Fred.HostedService.Services;
using Equibles.Fred.Repositories;
using Equibles.Integrations.Fred.Contracts;
using Equibles.Integrations.Fred.Models;
using Equibles.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.Tests.Fred;

public class FredImportServiceTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly FredSeriesRepository _seriesRepo;
    private readonly FredObservationRepository _obsRepo;
    private readonly IFredClient _fredClient;
    private readonly FredImportService _sut;

    public FredImportServiceTests() {
        _dbContext = TestDbContextFactory.Create(new FredModuleConfiguration());
        _seriesRepo = new FredSeriesRepository(_dbContext);
        _obsRepo = new FredObservationRepository(_dbContext);
        _fredClient = Substitute.For<IFredClient>();

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(FredSeriesRepository), _seriesRepo),
            (typeof(FredObservationRepository), _obsRepo)
        );

        var workerOptions = Options.Create(new WorkerOptions {
            MinSyncDate = new DateTime(2020, 1, 1)
        });

        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        _sut = new FredImportService(
            scopeFactory,
            Substitute.For<ILogger<FredImportService>>(),
            _fredClient,
            workerOptions,
            errorReporter
        );
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static FredSeriesRecord CreateMetadataRecord(
        string id = "FEDFUNDS",
        string title = "Federal Funds Effective Rate",
        string frequencyShort = "M",
        string units = "Percent",
        string seasonalAdjustmentShort = "NSA",
        string observationStart = "2020-01-01",
        string observationEnd = "2024-12-31") {
        return new FredSeriesRecord {
            Id = id,
            Title = title,
            FrequencyShort = frequencyShort,
            Units = units,
            SeasonalAdjustmentShort = seasonalAdjustmentShort,
            ObservationStart = observationStart,
            ObservationEnd = observationEnd,
        };
    }

    private static List<FredObservationRecord> CreateObservationRecords(params (string date, string value)[] records) {
        return records.Select(r => new FredObservationRecord { Date = r.date, Value = r.value }).ToList();
    }

    private void SetupApiForSeries(string seriesId, FredSeriesRecord metadata, List<FredObservationRecord> observations) {
        _fredClient.GetSeriesMetadata(seriesId).Returns(Task.FromResult(metadata));
        _fredClient.GetObservations(seriesId, Arg.Any<DateOnly?>()).Returns(Task.FromResult(observations));
    }

    private void SetupApiForAllOtherSeriesEmpty() {
        // Default: return null metadata for any series not explicitly configured
        _fredClient.GetSeriesMetadata(Arg.Any<string>()).Returns(Task.FromResult<FredSeriesRecord>(null));
        _fredClient.GetObservations(Arg.Any<string>(), Arg.Any<DateOnly?>()).Returns(Task.FromResult(new List<FredObservationRecord>()));
    }

    // ── ImportSeries: creates new series ──────────────────────────────

    [Fact]
    public async Task Import_NewSeries_CreatesSeriesInDb() {
        SetupApiForAllOtherSeriesEmpty();

        var metadata = CreateMetadataRecord("FEDFUNDS", "Federal Funds Effective Rate");
        var observations = CreateObservationRecords(
            ("2024-01-01", "5.33"),
            ("2024-01-02", "5.34")
        );
        SetupApiForSeries("FEDFUNDS", metadata, observations);

        await _sut.Import(CancellationToken.None);

        var series = await _seriesRepo.GetBySeriesId("FEDFUNDS").FirstOrDefaultAsync();
        series.Should().NotBeNull();
        series!.Title.Should().Be("Federal Funds Effective Rate");
        series.Category.Should().Be(FredSeriesCategory.InterestRates);
        series.Frequency.Should().Be("M");
        series.Units.Should().Be("Percent");
        series.SeasonalAdjustment.Should().Be("NSA");
    }

    [Fact]
    public async Task Import_NewSeries_ParsesObservationDates() {
        SetupApiForAllOtherSeriesEmpty();

        var metadata = CreateMetadataRecord("FEDFUNDS");
        metadata.ObservationStart = "2020-01-01";
        metadata.ObservationEnd = "2024-12-31";

        SetupApiForSeries("FEDFUNDS", metadata, CreateObservationRecords(("2024-06-01", "5.50")));

        await _sut.Import(CancellationToken.None);

        var series = await _seriesRepo.GetBySeriesId("FEDFUNDS").FirstOrDefaultAsync();
        series.Should().NotBeNull();
        series!.ObservationStart.Should().Be(new DateOnly(2020, 1, 1));
    }

    // ── ImportSeries: updates existing series metadata ────────────────

    [Fact]
    public async Task Import_ExistingSeries_UpdatesObservationEndAndLastUpdated() {
        SetupApiForAllOtherSeriesEmpty();

        // Pre-seed a series in DB
        var existingSeries = new FredSeries {
            SeriesId = "FEDFUNDS",
            Title = "Federal Funds Effective Rate",
            Category = FredSeriesCategory.InterestRates,
            Frequency = "M",
            Units = "Percent",
            SeasonalAdjustment = "NSA",
            ObservationStart = new DateOnly(2020, 1, 1),
            ObservationEnd = new DateOnly(2024, 6, 1),
            LastUpdated = null,
        };
        _dbContext.Set<FredSeries>().Add(existingSeries);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var observations = CreateObservationRecords(
            ("2024-07-01", "5.40"),
            ("2024-08-01", "5.35")
        );
        // Series already exists, so GetSeriesMetadata should not be called
        _fredClient.GetObservations("FEDFUNDS", Arg.Any<DateOnly?>()).Returns(Task.FromResult(observations));

        await _sut.Import(CancellationToken.None);

        var updated = await _seriesRepo.GetBySeriesId("FEDFUNDS").FirstOrDefaultAsync();
        updated.Should().NotBeNull();
        updated!.ObservationEnd.Should().Be(new DateOnly(2024, 8, 1));
        updated.LastUpdated.Should().NotBeNull();
        updated.LastUpdated.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    // ── ImportObservations: creates new observations ──────────────────

    [Fact]
    public async Task Import_WithObservations_PersistsObservationsToDb() {
        SetupApiForAllOtherSeriesEmpty();

        var metadata = CreateMetadataRecord("FEDFUNDS");
        var observations = CreateObservationRecords(
            ("2024-01-01", "5.33"),
            ("2024-01-02", "5.34"),
            ("2024-01-03", "5.35")
        );
        SetupApiForSeries("FEDFUNDS", metadata, observations);

        await _sut.Import(CancellationToken.None);

        var series = await _seriesRepo.GetBySeriesId("FEDFUNDS").FirstOrDefaultAsync();
        series.Should().NotBeNull();

        var dbObs = _obsRepo.GetBySeries(series!).ToList();
        dbObs.Should().HaveCount(3);
        dbObs.Should().Contain(o => o.Date == new DateOnly(2024, 1, 1) && o.Value == 5.33m);
        dbObs.Should().Contain(o => o.Date == new DateOnly(2024, 1, 2) && o.Value == 5.34m);
        dbObs.Should().Contain(o => o.Date == new DateOnly(2024, 1, 3) && o.Value == 5.35m);
    }

    // ── ImportObservations: skips duplicates ──────────────────────────

    [Fact]
    public async Task Import_DuplicateObservations_SkipsDuplicatesAndInsertsOnlyNew() {
        SetupApiForAllOtherSeriesEmpty();

        // Pre-seed series and one observation
        var existingSeries = new FredSeries {
            SeriesId = "FEDFUNDS",
            Title = "Federal Funds Effective Rate",
            Category = FredSeriesCategory.InterestRates,
            Frequency = "M",
            Units = "Percent",
            SeasonalAdjustment = "NSA",
        };
        _dbContext.Set<FredSeries>().Add(existingSeries);
        _dbContext.Set<FredObservation>().Add(new FredObservation {
            FredSeriesId = existingSeries.Id,
            Date = new DateOnly(2024, 1, 1),
            Value = 5.33m,
        });
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        // API returns the existing date plus a new one
        var observations = CreateObservationRecords(
            ("2024-01-01", "5.33"), // duplicate
            ("2024-01-02", "5.34")  // new
        );
        _fredClient.GetObservations("FEDFUNDS", Arg.Any<DateOnly?>()).Returns(Task.FromResult(observations));

        await _sut.Import(CancellationToken.None);

        var series = await _seriesRepo.GetBySeriesId("FEDFUNDS").FirstOrDefaultAsync();
        var dbObs = _obsRepo.GetBySeries(series!).ToList();
        dbObs.Should().HaveCount(2);
        dbObs.Should().Contain(o => o.Date == new DateOnly(2024, 1, 1));
        dbObs.Should().Contain(o => o.Date == new DateOnly(2024, 1, 2));
    }

    // ── Import: handles empty API response ───────────────────────────

    [Fact]
    public async Task Import_EmptyObservationResponse_InsertsNoObservations() {
        SetupApiForAllOtherSeriesEmpty();

        var metadata = CreateMetadataRecord("FEDFUNDS");
        SetupApiForSeries("FEDFUNDS", metadata, []);

        await _sut.Import(CancellationToken.None);

        var series = await _seriesRepo.GetBySeriesId("FEDFUNDS").FirstOrDefaultAsync();
        series.Should().NotBeNull();

        var dbObs = _obsRepo.GetBySeries(series!).ToList();
        dbObs.Should().BeEmpty();
    }

    // ── Import: handles null metadata (series not found in API) ──────

    [Fact]
    public async Task Import_NullMetadata_SkipsSeriesWithoutCreating() {
        SetupApiForAllOtherSeriesEmpty();
        // All series return null metadata -- none should be created

        await _sut.Import(CancellationToken.None);

        var allSeries = _seriesRepo.GetAll().ToList();
        allSeries.Should().BeEmpty();
    }

    // ── Import: handles missing values (FRED "." notation) ──────────

    [Fact]
    public async Task Import_MissingValueDot_StoresNullValue() {
        SetupApiForAllOtherSeriesEmpty();

        var metadata = CreateMetadataRecord("FEDFUNDS");
        var observations = CreateObservationRecords(
            ("2024-01-01", "."),
            ("2024-01-02", "5.34")
        );
        SetupApiForSeries("FEDFUNDS", metadata, observations);

        await _sut.Import(CancellationToken.None);

        var series = await _seriesRepo.GetBySeriesId("FEDFUNDS").FirstOrDefaultAsync();
        var dbObs = _obsRepo.GetBySeries(series!).OrderBy(o => o.Date).ToList();

        dbObs.Should().HaveCount(2);
        dbObs[0].Value.Should().BeNull();
        dbObs[1].Value.Should().Be(5.34m);
    }

    // ── Import: handles unparseable dates ────────────────────────────

    [Fact]
    public async Task Import_UnparseableDate_SkipsRecord() {
        SetupApiForAllOtherSeriesEmpty();

        var metadata = CreateMetadataRecord("FEDFUNDS");
        var observations = CreateObservationRecords(
            ("not-a-date", "5.33"),
            ("2024-01-02", "5.34")
        );
        SetupApiForSeries("FEDFUNDS", metadata, observations);

        await _sut.Import(CancellationToken.None);

        var series = await _seriesRepo.GetBySeriesId("FEDFUNDS").FirstOrDefaultAsync();
        var dbObs = _obsRepo.GetBySeries(series!).ToList();

        dbObs.Should().ContainSingle();
        dbObs[0].Date.Should().Be(new DateOnly(2024, 1, 2));
    }

    // ── Import: all dates unparseable returns early ──────────────────

    [Fact]
    public async Task Import_AllDatesUnparseable_InsertsNoObservations() {
        SetupApiForAllOtherSeriesEmpty();

        var metadata = CreateMetadataRecord("FEDFUNDS");
        var observations = CreateObservationRecords(
            ("bad-date", "5.33"),
            ("also-bad", "5.34")
        );
        SetupApiForSeries("FEDFUNDS", metadata, observations);

        await _sut.Import(CancellationToken.None);

        var series = await _seriesRepo.GetBySeriesId("FEDFUNDS").FirstOrDefaultAsync();
        series.Should().NotBeNull();

        var dbObs = _obsRepo.GetBySeries(series!).ToList();
        dbObs.Should().BeEmpty();
    }

    // ── Import: respects cancellation ────────────────────────────────

    [Fact]
    public async Task Import_CancellationRequested_ThrowsOperationCanceledException() {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _sut.Import(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Import: HttpRequestException skips series ────────────────────

    [Fact]
    public async Task Import_HttpRequestException_SkipsSeriesAndContinues() {
        SetupApiForAllOtherSeriesEmpty();

        // FEDFUNDS throws HttpRequestException (first in CuratedSeriesRegistry)
        _fredClient.GetSeriesMetadata("FEDFUNDS").Returns<FredSeriesRecord>(
            _ => throw new HttpRequestException("API down")
        );

        // EFFR succeeds (second in CuratedSeriesRegistry, same category)
        var metadata = CreateMetadataRecord("EFFR", "Effective Federal Funds Rate");
        var observations = CreateObservationRecords(("2024-01-01", "5.33"));
        SetupApiForSeries("EFFR", metadata, observations);

        await _sut.Import(CancellationToken.None);

        // FEDFUNDS should not be created
        var fedfunds = await _seriesRepo.GetBySeriesId("FEDFUNDS").FirstOrDefaultAsync();
        fedfunds.Should().BeNull();

        // EFFR should be created despite the earlier failure
        var effr = await _seriesRepo.GetBySeriesId("EFFR").FirstOrDefaultAsync();
        effr.Should().NotBeNull();
    }

    // ── Import: unparseable value stored as null ─────────────────────

    [Fact]
    public async Task Import_UnparseableValue_StoresNullValue() {
        SetupApiForAllOtherSeriesEmpty();

        var metadata = CreateMetadataRecord("FEDFUNDS");
        var observations = CreateObservationRecords(
            ("2024-01-01", "not-a-number"),
            ("2024-01-02", "5.34")
        );
        SetupApiForSeries("FEDFUNDS", metadata, observations);

        await _sut.Import(CancellationToken.None);

        var series = await _seriesRepo.GetBySeriesId("FEDFUNDS").FirstOrDefaultAsync();
        var dbObs = _obsRepo.GetBySeries(series!).OrderBy(o => o.Date).ToList();

        dbObs.Should().HaveCount(2);
        dbObs[0].Value.Should().BeNull();
        dbObs[1].Value.Should().Be(5.34m);
    }

    // ── Import: series already up to date ────────────────────────────

    [Fact]
    public async Task Import_SeriesUpToDate_SkipsFetchingObservations() {
        SetupApiForAllOtherSeriesEmpty();

        // Pre-seed series with an observation dated today (so startDate > today)
        var existingSeries = new FredSeries {
            SeriesId = "FEDFUNDS",
            Title = "Federal Funds Effective Rate",
            Category = FredSeriesCategory.InterestRates,
            Frequency = "M",
            Units = "Percent",
            SeasonalAdjustment = "NSA",
        };
        _dbContext.Set<FredSeries>().Add(existingSeries);
        _dbContext.Set<FredObservation>().Add(new FredObservation {
            FredSeriesId = existingSeries.Id,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            Value = 5.33m,
        });
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        await _sut.Import(CancellationToken.None);

        // GetObservations should not have been called for FEDFUNDS
        await _fredClient.DidNotReceive().GetObservations("FEDFUNDS", Arg.Any<DateOnly?>());
    }

    // ── Import: observation start date determined by latest in DB ────

    [Fact]
    public async Task Import_ExistingObservations_FetchesFromDayAfterLatest() {
        SetupApiForAllOtherSeriesEmpty();

        var existingSeries = new FredSeries {
            SeriesId = "FEDFUNDS",
            Title = "Federal Funds Effective Rate",
            Category = FredSeriesCategory.InterestRates,
            Frequency = "M",
            Units = "Percent",
            SeasonalAdjustment = "NSA",
        };
        _dbContext.Set<FredSeries>().Add(existingSeries);
        _dbContext.Set<FredObservation>().Add(new FredObservation {
            FredSeriesId = existingSeries.Id,
            Date = new DateOnly(2024, 6, 1),
            Value = 5.33m,
        });
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var observations = CreateObservationRecords(("2024-06-02", "5.34"));
        _fredClient.GetObservations("FEDFUNDS", Arg.Any<DateOnly?>()).Returns(Task.FromResult(observations));

        await _sut.Import(CancellationToken.None);

        // Verify it called GetObservations with startDate = 2024-06-02 (day after latest)
        await _fredClient.Received().GetObservations("FEDFUNDS", new DateOnly(2024, 6, 2));
    }

    // ── Import: decimal precision preserved ──────────────────────────

    [Fact]
    public async Task Import_DecimalValues_PreservesFullPrecision() {
        SetupApiForAllOtherSeriesEmpty();

        var metadata = CreateMetadataRecord("FEDFUNDS");
        var observations = CreateObservationRecords(
            ("2024-01-01", "5.123456789")
        );
        SetupApiForSeries("FEDFUNDS", metadata, observations);

        await _sut.Import(CancellationToken.None);

        var series = await _seriesRepo.GetBySeriesId("FEDFUNDS").FirstOrDefaultAsync();
        var dbObs = _obsRepo.GetBySeries(series!).ToList();

        dbObs.Should().ContainSingle();
        dbObs[0].Value.Should().Be(5.123456789m);
    }

    // ── Import: negative values handled correctly ────────────────────

    [Fact]
    public async Task Import_NegativeValues_StoredCorrectly() {
        SetupApiForAllOtherSeriesEmpty();

        var metadata = CreateMetadataRecord("T10Y2Y", "10-Year Treasury Minus 2-Year");
        var observations = CreateObservationRecords(
            ("2024-01-01", "-0.42")
        );
        SetupApiForSeries("T10Y2Y", metadata, observations);

        await _sut.Import(CancellationToken.None);

        var series = await _seriesRepo.GetBySeriesId("T10Y2Y").FirstOrDefaultAsync();
        var dbObs = _obsRepo.GetBySeries(series!).ToList();

        dbObs.Should().ContainSingle();
        dbObs[0].Value.Should().Be(-0.42m);
    }

    // ── Import: series category from CuratedSeriesRegistry ──────────

    [Fact]
    public async Task Import_SeriesCategory_SetFromCuratedRegistry() {
        SetupApiForAllOtherSeriesEmpty();

        // T10Y2Y is categorized as YieldSpreads in the registry
        var metadata = CreateMetadataRecord("T10Y2Y", "10-Year Treasury Minus 2-Year");
        SetupApiForSeries("T10Y2Y", metadata, []);

        await _sut.Import(CancellationToken.None);

        var series = await _seriesRepo.GetBySeriesId("T10Y2Y").FirstOrDefaultAsync();
        series.Should().NotBeNull();
        series!.Category.Should().Be(FredSeriesCategory.YieldSpreads);
    }
}
