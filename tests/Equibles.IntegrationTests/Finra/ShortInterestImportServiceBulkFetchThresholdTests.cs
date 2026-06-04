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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Finra;

/// <summary>
/// FetchMissingRecords picks bulk fetch when ALL tracked stocks are missing OR when the
/// missing count exceeds the filtered-fetch threshold (500). The pipeline test covers the
/// all-missing arm and the filtered-fetch test the small-subset arm; this pins the
/// distinct second arm — more than 500 missing but NOT all — which must still bulk-fetch so
/// a 500+ symbol query is never issued. 502 tracked, 501 missing isolates that arm (501 > 500
/// yet 501 != 502), so dropping it would wrongly fall through to the symbol-scoped overload.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ShortInterestImportServiceBulkFetchThresholdTests : ParadeDbMcpTestBase
{
    public ShortInterestImportServiceBulkFetchThresholdTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Import_MoreThanThresholdMissingButNotAll_UsesBulkFetch()
    {
        var settlementDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        var stocks = new List<CommonStock>();
        for (var i = 0; i < 502; i++)
        {
            stocks.Add(
                new CommonStock
                {
                    Cik = i.ToString("D10"),
                    Ticker = $"T{i:D4}",
                    Name = $"Tracked {i}",
                }
            );
        }
        DbContext.AddRange(stocks);
        // Only the first stock has data for the date → 501 of 502 are missing: above the
        // 500 threshold but a strict subset, so only the threshold arm makes the fetch bulk.
        DbContext.Add(
            new ShortInterest
            {
                CommonStockId = stocks[0].Id,
                SettlementDate = settlementDate,
                CurrentShortPosition = 1,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var finraClient = Substitute.For<IFinraClient>();
        finraClient
            .GetShortInterestSettlementDatesAfter(Arg.Any<DateOnly>())
            .Returns(new List<DateOnly>());
        finraClient.GetShortInterest(settlementDate).Returns(new List<ShortInterestRecord>());

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(CommonStockRepository), new CommonStockRepository(DbContext)),
            (typeof(ShortInterestRepository), new ShortInterestRepository(DbContext))
        );
        var sut = new ShortInterestImportService(
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

        await sut.Import(CancellationToken.None);

        await finraClient.Received().GetShortInterest(settlementDate);
        await finraClient
            .DidNotReceive()
            .GetShortInterest(Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<string>>());
    }
}
