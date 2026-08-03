using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Finra.Data;
using Equibles.Finra.Data.Models;
using Equibles.Finra.HostedService.Services;
using Equibles.Finra.Repositories;
using Equibles.Integrations.Finra.Contracts;
using Equibles.Integrations.Finra.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Finra;

/// <summary>
/// Pins partition-based reconciliation for FINRA's staggered weekly publication.
/// </summary>
public class OffExchangeVolumeImportServiceBackfillTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 3, 30, 20, 0, 0, TimeSpan.Zero);

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly OffExchangeVolumeRepository _volumeRepo;
    private readonly FinraImportPartitionRepository _partitionRepo;
    private readonly CommonStockRepository _stockRepo;
    private readonly IFinraClient _finraClient;
    private readonly WorkerOptions _workerOptions;
    private readonly TimeProvider _timeProvider;
    private readonly OffExchangeVolumeImportService _service;

    public OffExchangeVolumeImportServiceBackfillTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new FinraModuleConfiguration()
        );
        _volumeRepo = new OffExchangeVolumeRepository(_dbContext);
        _partitionRepo = new FinraImportPartitionRepository(_dbContext);
        _stockRepo = new CommonStockRepository(_dbContext);
        _finraClient = Substitute.For<IFinraClient>();
        _workerOptions = new WorkerOptions();
        _timeProvider = Substitute.For<TimeProvider>();
        _timeProvider.GetUtcNow().Returns(Now);

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(OffExchangeVolumeRepository), _volumeRepo),
            (typeof(CommonStockRepository), _stockRepo)
        );

        _service = new OffExchangeVolumeImportService(
            scopeFactory,
            Substitute.For<ILogger<OffExchangeVolumeImportService>>(),
            _finraClient,
            new TickerMapService(scopeFactory),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(_workerOptions),
            new FinraImportPartitionTracker(_partitionRepo),
            _timeProvider
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    // Monday that starts the FINRA reporting week containing the given date — mirrors the
    // private ToWeekStart in the service.
    private static DateOnly WeekStart(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    [Fact]
    public async Task Import_StoredWeekBetweenFloorAndToday_BackfillsEarlierSkipsStoredAndFetchesForward()
    {
        var apple = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "CIK-AAPL",
        };
        _stockRepo.AddRange([apple]);
        await _stockRepo.SaveChanges();

        var currentWeek = WeekStart(DateOnly.FromDateTime(Now.UtcDateTime));
        var storedWeek = currentWeek.AddDays(-14); // two weeks back
        var backfillWeek = currentWeek.AddDays(-21); // below the stored week
        var forwardWeek = currentWeek.AddDays(-7); // above the stored week

        _dbContext
            .Set<OffExchangeVolume>()
            .Add(
                new OffExchangeVolume
                {
                    CommonStockId = apple.Id,
                    WeekStartDate = storedWeek,
                    AtsVolume = 1,
                    NonAtsOtcVolume = 1,
                }
            );
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        await SeedCompletedPartition(storedWeek);

        _workerOptions.MinSyncDate = backfillWeek.ToDateTime(TimeOnly.MinValue);

        _finraClient
            .GetWeeklyOffExchangeVolume(Arg.Any<DateOnly>())
            .Returns(new List<OffExchangeWeeklyRecord>());
        _finraClient
            .GetWeeklyOffExchangeVolume(backfillWeek)
            .Returns(MakeRecords(ats: 5_000, otc: 3_000));
        _finraClient
            .GetWeeklyOffExchangeVolume(forwardWeek)
            .Returns(MakeRecords(ats: 7_000, otc: 2_000));

        await _service.Import(CancellationToken.None);

        await _finraClient.DidNotReceive().GetWeeklyOffExchangeVolume(storedWeek);
        await _finraClient.Received().GetWeeklyOffExchangeVolume(backfillWeek);
        await _finraClient.Received().GetWeeklyOffExchangeVolume(forwardWeek);

        var rows = _volumeRepo.GetAll().Where(v => v.CommonStockId == apple.Id).ToList();
        rows.Should().Contain(v => v.WeekStartDate == backfillWeek && v.AtsVolume == 5_000);
        rows.Should().Contain(v => v.WeekStartDate == forwardWeek && v.AtsVolume == 7_000);
    }

    [Fact]
    public async Task Import_TierOneOnly_RetriesUntilAllDelayedTiersArePublished()
    {
        var apple = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "CIK-AAPL",
        };
        _stockRepo.AddRange([apple]);
        await _stockRepo.SaveChanges();

        var week = WeekStart(DateOnly.FromDateTime(Now.UtcDateTime)).AddDays(-28);
        _workerOptions.MinSyncDate = week.ToDateTime(TimeOnly.MinValue);
        _finraClient
            .GetWeeklyOffExchangeVolume(Arg.Any<DateOnly>())
            .Returns(new List<OffExchangeWeeklyRecord>());
        _finraClient
            .GetWeeklyOffExchangeVolume(week)
            .Returns(MakeTierOneRecords(ats: 1_000, otc: 500), MakeRecords(ats: 5_000, otc: 3_000));

        await _service.Import(CancellationToken.None);

        _partitionRepo.GetPartition("off-exchange-weekly-v1", "all", week).Should().BeEmpty();
        _volumeRepo.GetByWeek(week).Should().BeEmpty();

        await _service.Import(CancellationToken.None);

        var row = _volumeRepo.GetByWeek(week).Single(v => v.CommonStockId == apple.Id);
        row.AtsVolume.Should().Be(5_000);
        row.NonAtsOtcVolume.Should().Be(3_000);
        _partitionRepo.GetPartition("off-exchange-weekly-v1", "all", week).Should().ContainSingle();

        _finraClient.ClearReceivedCalls();
        await _service.Import(CancellationToken.None);
        await _finraClient.DidNotReceive().GetWeeklyOffExchangeVolume(week);
    }

    [Fact]
    public async Task Import_PartialTierRefresh_PreservesExistingCompleteWeek()
    {
        var apple = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "CIK-AAPL",
        };
        _stockRepo.AddRange([apple]);
        await _stockRepo.SaveChanges();

        var week = WeekStart(DateOnly.FromDateTime(Now.UtcDateTime)).AddDays(-28);
        var previousImport = Now.UtcDateTime.AddDays(-2);
        _workerOptions.MinSyncDate = week.ToDateTime(TimeOnly.MinValue);
        _dbContext
            .Set<OffExchangeVolume>()
            .Add(
                new OffExchangeVolume
                {
                    CommonStockId = apple.Id,
                    WeekStartDate = week,
                    AtsVolume = 9_000,
                    AtsTradeCount = 90,
                    NonAtsOtcVolume = 4_000,
                    NonAtsOtcTradeCount = 40,
                }
            );
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        await SeedCompletedPartition(week, previousImport);

        _finraClient
            .GetWeeklyOffExchangeVolume(Arg.Any<DateOnly>())
            .Returns(new List<OffExchangeWeeklyRecord>());
        _finraClient
            .GetWeeklyOffExchangeVolume(week)
            .Returns(MakeTierOneRecords(ats: 1_000, otc: 500));

        await _service.Import(CancellationToken.None);

        var row = _volumeRepo.GetByWeek(week).Single(v => v.CommonStockId == apple.Id);
        row.AtsVolume.Should().Be(9_000);
        row.AtsTradeCount.Should().Be(90);
        row.NonAtsOtcVolume.Should().Be(4_000);
        row.NonAtsOtcTradeCount.Should().Be(40);
        _partitionRepo
            .GetPartition("off-exchange-weekly-v1", "all", week)
            .Single()
            .ImportedAt.Should()
            .Be(previousImport);
    }

    private async Task SeedCompletedPartition(DateOnly week, DateTime? importedAt = null)
    {
        _partitionRepo.Add(
            new FinraImportPartition
            {
                Dataset = "off-exchange-weekly-v1",
                PartitionDate = week,
                ScopeKey = "all",
                ImportedAt = importedAt ?? Now.UtcDateTime,
            }
        );
        await _partitionRepo.SaveChanges();
        _dbContext.ChangeTracker.Clear();
    }

    private static List<OffExchangeWeeklyRecord> MakeRecords(long ats, long otc) =>
        [
            new()
            {
                Symbol = "AAPL",
                TierIdentifier = "T1",
                SummaryTypeCode = "ATS_W_SMBL",
                TotalWeeklyShareQuantity = ats,
                TotalWeeklyTradeCount = 10,
            },
            new()
            {
                Symbol = "AAPL",
                TierIdentifier = "T2",
                SummaryTypeCode = "OTC_W_SMBL",
                TotalWeeklyShareQuantity = otc,
                TotalWeeklyTradeCount = 5,
            },
            new()
            {
                Symbol = "AAPL",
                TierIdentifier = "OTCE",
                SummaryTypeCode = "OTC_W_SMBL",
                TotalWeeklyShareQuantity = 0,
                TotalWeeklyTradeCount = 0,
            },
        ];

    private static List<OffExchangeWeeklyRecord> MakeTierOneRecords(long ats, long otc) =>
        [
            new()
            {
                Symbol = "AAPL",
                TierIdentifier = "T1",
                SummaryTypeCode = "ATS_W_SMBL",
                TotalWeeklyShareQuantity = ats,
                TotalWeeklyTradeCount = 10,
            },
            new()
            {
                Symbol = "AAPL",
                TierIdentifier = "T1",
                SummaryTypeCode = "OTC_W_SMBL",
                TotalWeeklyShareQuantity = otc,
                TotalWeeklyTradeCount = 5,
            },
        ];

    [Fact]
    public async Task Import_FloorWeekInFuture_DoesNothing()
    {
        _workerOptions.MinSyncDate = new DateTime(2099, 1, 1);

        await _service.Import(CancellationToken.None);

        await _finraClient.DidNotReceive().GetWeeklyOffExchangeVolume(Arg.Any<DateOnly>());
        _volumeRepo.GetAll().Should().BeEmpty();
    }
}
