using Equibles.CommonStocks.Data;
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
        var predicate = BacktestPriceLoader
            .ListingPredicate([
                new BacktestPriceLoader.ListingKey(firstId, "BRK-A"),
                new BacktestPriceLoader.ListingKey(secondId, "GOOG"),
            ])
            .Compile();

        predicate(new DailyStockPrice { CommonStockId = firstId, ListedTicker = "BRK-A" })
            .Should()
            .BeTrue();
        predicate(new DailyStockPrice { CommonStockId = firstId, ListedTicker = "GOOG" })
            .Should()
            .BeFalse("independent stock and ticker filters would admit this cross-pair");
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
            .Select(index => new BacktestPriceLoader.ListingKey(Guid.NewGuid(), $"CLASS-{index}"))
            .ToArray();
        var batches = keys.Chunk(BacktestPriceLoader.ListingQueryBatchSize).ToArray();

        batches.Should().HaveCount(3);
        batches
            .Should()
            .OnlyContain(batch => batch.Length <= BacktestPriceLoader.ListingQueryBatchSize);
        foreach (var batch in batches)
        {
            var predicate = BacktestPriceLoader.ListingPredicate(batch);
            var act = () => context.Set<DailyStockPrice>().Where(predicate).ToQueryString();

            act.Should().NotThrow<InvalidOperationException>();
            act().Should().Contain("ListedTicker");
        }
    }
}
