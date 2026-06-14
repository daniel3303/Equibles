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
/// Pins the historical-backfill behaviour of <see cref="OffExchangeVolumeImportService.Import"/>:
/// the importer now scans the whole [floor, current week] window and skips only the weeks
/// already stored, so a fresh deployment fills weeks below the earliest stored week instead of
/// only ever moving forward from the latest one.
/// </summary>
public class OffExchangeVolumeImportServiceBackfillTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly OffExchangeVolumeRepository _volumeRepo;
    private readonly CommonStockRepository _stockRepo;
    private readonly IFinraClient _finraClient;
    private readonly WorkerOptions _workerOptions;
    private readonly OffExchangeVolumeImportService _service;

    public OffExchangeVolumeImportServiceBackfillTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new FinraModuleConfiguration()
        );
        _volumeRepo = new OffExchangeVolumeRepository(_dbContext);
        _stockRepo = new CommonStockRepository(_dbContext);
        _finraClient = Substitute.For<IFinraClient>();
        _workerOptions = new WorkerOptions();

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
            Options.Create(_workerOptions)
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

        var currentWeek = WeekStart(DateOnly.FromDateTime(DateTime.UtcNow));
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

        // Floor a week below the stored week, so the loop must backfill the earlier week,
        // skip the stored week, and still import forward past it.
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

        // The stored week is never re-fetched...
        await _finraClient.DidNotReceive().GetWeeklyOffExchangeVolume(storedWeek);
        // ...but both the earlier (backfill) and later (forward) weeks are.
        await _finraClient.Received().GetWeeklyOffExchangeVolume(backfillWeek);
        await _finraClient.Received().GetWeeklyOffExchangeVolume(forwardWeek);

        var rows = _volumeRepo.GetAll().Where(v => v.CommonStockId == apple.Id).ToList();
        rows.Should().Contain(v => v.WeekStartDate == backfillWeek && v.AtsVolume == 5_000);
        rows.Should().Contain(v => v.WeekStartDate == forwardWeek && v.AtsVolume == 7_000);
    }

    private static List<OffExchangeWeeklyRecord> MakeRecords(long ats, long otc) =>
        [
            new()
            {
                Symbol = "AAPL",
                SummaryTypeCode = "ATS_W_SMBL",
                TotalWeeklyShareQuantity = ats,
                TotalWeeklyTradeCount = 10,
            },
            new()
            {
                Symbol = "AAPL",
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
