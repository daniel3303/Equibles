using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Holdings;

public class BacktestPriceLoaderListingPredicateTests
{
    [Fact]
    public void ListingPredicate_MatchesOnlyRequestedStockAndListingPairs()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var predicate = BacktestPriceLoader.ListingPredicate([firstId, secondId]).Compile();

        predicate(new EquityDailyStockPrice { EquityListingId = firstId, SourceTicker = "BRK-A" })
            .Should()
            .BeTrue();
        predicate(
                new EquityDailyStockPrice
                {
                    EquityListingId = Guid.NewGuid(),
                    SourceTicker = "BRK-A",
                }
            )
            .Should()
            .BeFalse("the same ticker on another listing must not match");
    }

    [Fact]
    public void ListingPredicate_PortfolioScaleBatchesRemainBoundedAndTranslate()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseNpgsql("Host=localhost;Database=translation-only", options => options.UseVector())
            .EnableServiceProviderCaching(false)
            .Options;
        using var context = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new YahooModuleConfiguration(),
            }
        );
        var keys = Enumerable
            .Range(0, BacktestPriceLoader.ListingQueryBatchSize * 2 + 1)
            .Select(index => Guid.NewGuid())
            .ToArray();
        var batches = keys.Chunk(BacktestPriceLoader.ListingQueryBatchSize).ToArray();

        batches.Should().HaveCount(3);
        batches
            .Should()
            .OnlyContain(batch => batch.Length <= BacktestPriceLoader.ListingQueryBatchSize);
        foreach (var batch in batches)
        {
            var predicate = BacktestPriceLoader.ListingPredicate(batch);
            var act = () => context.Set<EquityDailyStockPrice>().Where(predicate).ToQueryString();

            act.Should().NotThrow<InvalidOperationException>();
            act().Should().Contain("EquityListingId");
        }
    }
}
