using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Finra.Data.Models;
using Equibles.Finra.HostedService.Services;
using Equibles.Finra.Repositories;
using Equibles.Integrations.Finra.Contracts;
using Equibles.Integrations.Finra.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Finra;

/// <summary>
/// <see cref="ShortInterestImportService.Import"/> (the single largest method
/// gap) was almost entirely uncovered. These drive the full discovery →
/// per-date missing-stock diff → bulk fetch → live-id-validated batch persist
/// pipeline end-to-end, plus the settlement-date discovery catch arm.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ShortInterestImportServicePipelineTests : ParadeDbMcpTestBase
{
    public ShortInterestImportServicePipelineTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private CommonStock _stock;

    private async Task SeedStock()
    {
        _stock = new CommonStock
        {
            Cik = "0000000888",
            Ticker = "TESTI",
            Name = "Short Interest Test Inc.",
        };
        DbContext.Add(_stock);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private ShortInterestImportService BuildService(IFinraClient finraClient)
    {
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(CommonStockRepository), new CommonStockRepository(DbContext)),
            (typeof(ShortInterestRepository), new ShortInterestRepository(DbContext))
        );
        return new ShortInterestImportService(
            scopeFactory,
            Substitute.For<ILogger<ShortInterestImportService>>(),
            finraClient,
            new TickerMapService(scopeFactory),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(
                new WorkerOptions { TickersToSync = [], MinSyncDate = DateTime.UtcNow.AddDays(-30) }
            )
        );
    }

    [Fact]
    public async Task Import_NewSettlementDateWithMissingStock_BulkFetchesAndPersists()
    {
        await SeedStock();
        var settlementDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        var finraClient = Substitute.For<IFinraClient>();
        finraClient.GetShortInterestSettlementDates().Returns([settlementDate]);
        finraClient
            .GetShortInterest(settlementDate)
            .Returns(
                new List<ShortInterestRecord>
                {
                    new()
                    {
                        Symbol = "TESTI",
                        CurrentShortPosition = 500_000,
                        PreviousShortPosition = 400_000,
                        ChangeInShortPosition = 100_000,
                        AverageDailyVolume = 1_000_000,
                        DaysToCover = 0.5m,
                    },
                    // Unmatched symbol → filtered out by the tickerMap guard.
                    new() { Symbol = "NOPE", CurrentShortPosition = 1 },
                }
            );

        await BuildService(finraClient).Import(CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        var rows = await verify
            .Set<ShortInterest>()
            .AsNoTracking()
            .Where(s => s.CommonStockId == _stock.Id && s.SettlementDate == settlementDate)
            .ToListAsync();
        rows.Should().ContainSingle("the tracked stock's short interest must be persisted");
        rows[0].CurrentShortPosition.Should().Be(500_000);
        rows[0].DaysToCover.Should().Be(0.5m);
    }

    [Fact]
    public async Task Import_SettlementDateDiscoveryThrows_ReportsErrorAndReturns()
    {
        await SeedStock();

        var finraClient = Substitute.For<IFinraClient>();
        finraClient
            .GetShortInterestSettlementDates()
            .Returns<List<DateOnly>>(_ => throw new HttpRequestException("FINRA discovery down"));

        await BuildService(finraClient).Import(CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        var any = await verify.Set<ShortInterest>().AsNoTracking().AnyAsync();
        any.Should().BeFalse("discovery failed before any date was processed");
    }
}
