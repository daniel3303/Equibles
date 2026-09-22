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

/// <summary>Large historical gaps should use a bounded symbol filter instead of fetching the whole market.</summary>
[Collection(ParadeDbCollection.Name)]
public class ShortInterestImportServiceBulkFetchThresholdTests : ParadeDbMcpTestBase
{
    public ShortInterestImportServiceBulkFetchThresholdTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Import_501MissingListings_UsesFilteredFetch()
    {
        var settlementDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-60);

        var stocks = new List<EquityIssuer>();
        for (var i = 0; i < 502; i++)
        {
            stocks.Add(
                Equibles.TestSupport.EquityIssuerSeed.Create(
                    Cik: i.ToString("D10"),
                    Ticker: $"T{i:D4}",
                    Name: $"Tracked {i}"
                )
            );
        }
        DbContext.AddRange(stocks);
        // Only the first stock has data: 501 missing listings fit in the expanded filter.
        DbContext.Add(
            new ShortInterest
            {
                EquityListingId = Equibles
                    .TestSupport.NativeListingSeed.ForStock(
                        DbContext,
                        stocks[0],
                        stocks[0].Presentation.Listing.Ticker
                    )
                    .Id,
                ListedTicker = stocks[0].Presentation.Listing.Ticker,
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
            (typeof(EquityIssuerRepository), new EquityIssuerRepository(DbContext)),
            (typeof(EquityListingRepository), new EquityListingRepository(DbContext)),
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
                new WorkerOptions { TickersToSync = [], MinSyncDate = DateTime.UtcNow.AddDays(-90) }
            )
        );

        await sut.Import(CancellationToken.None);

        await finraClient.DidNotReceive().GetShortInterest(settlementDate);
        await finraClient
            .Received(1)
            .GetShortInterest(
                settlementDate,
                Arg.Is<IReadOnlyList<string>>(symbols => symbols.Count == 501)
            );
    }
}
