using System.Reflection;
using Equibles.CommonStocks.Data;
using Equibles.Data;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.IntegrationTests.InsiderTrading;

public class InsiderTransactionPriceBackfillManagerFetchClosesWeekendFallbackTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly InsiderTransactionPriceBackfillManager _manager;

    public InsiderTransactionPriceBackfillManagerFetchClosesWeekendFallbackTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new InsiderTradingModuleConfiguration(),
            new YahooModuleConfiguration()
        );
        _manager = new InsiderTransactionPriceBackfillManager(
            new InsiderTransactionRepository(_dbContext),
            new DailyStockPriceRepository(_dbContext),
            new InsiderTransactionPriceValidator(),
            _dbContext,
            NullLogger<InsiderTransactionPriceBackfillManager>.Instance
        );
    }

    public void Dispose() => _dbContext.Dispose();

    // Contract (FetchCloses doc): one close per (stock, date) = "the most recent
    // Close on or before that date" — the weekend/holiday fallback the manager
    // relies on. A transaction filed on a Saturday must resolve to the close of
    // the most recent prior *trading* day (Friday), not an earlier in-range day
    // (Thursday) and not nothing. Oracle derived from the doc, not the body.
    [Fact]
    public async Task FetchCloses_TransactionOnWeekend_UsesMostRecentPriorTradingDayClose()
    {
        var stockId = Guid.NewGuid();
        var thursday = new DateOnly(2024, 6, 13);
        var friday = new DateOnly(2024, 6, 14);
        var saturday = new DateOnly(2024, 6, 15);

        _dbContext
            .Set<DailyStockPrice>()
            .AddRange(
                new DailyStockPrice
                {
                    CommonStockId = stockId,
                    Date = thursday,
                    Close = 48m,
                },
                new DailyStockPrice
                {
                    CommonStockId = stockId,
                    Date = friday,
                    Close = 50m,
                }
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var batch = new List<InsiderTransaction>
        {
            new()
            {
                Id = Guid.NewGuid(),
                CommonStockId = stockId,
                TransactionDate = saturday,
            },
        };

        var method = typeof(InsiderTransactionPriceBackfillManager).GetMethod(
            "FetchCloses",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var closes = await (Task<Dictionary<(Guid, DateOnly), decimal>>)
            method.Invoke(_manager, [batch]);

        closes.Should().ContainKey((stockId, saturday));
        closes[(stockId, saturday)].Should().Be(50m);
    }
}
