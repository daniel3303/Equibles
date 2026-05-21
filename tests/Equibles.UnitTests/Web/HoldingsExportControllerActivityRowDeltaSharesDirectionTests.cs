using System.Collections;
using System.Reflection;
using Equibles.Holdings.Repositories.Models;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class HoldingsExportControllerActivityRowDeltaSharesDirectionTests
{
    // ActivityRow re-computes column 7 inline as `row.CurrentShares - row.PreviousShares`
    // rather than reading the model's `DeltaShares` property. A refactor that swaps the
    // operands (current - previous → previous - current) would silently invert the sign on
    // every TopBuys / TopSells row in the Activity CSV: a 1000→1400 buy would render as
    // -400 and an analyst's screen would flag it as a sell. Pin the direction with a
    // clear buy.
    [Fact]
    public void ActivityRow_BuyWithCurrentSharesGreaterThanPrevious_DeltaSharesIsPositive()
    {
        var stockId = Guid.NewGuid();
        var activity = new MarketWideStockActivity
        {
            CommonStockId = stockId,
            CurrentShares = 1000,
            PreviousShares = 400,
            CurrentValue = 10_000,
            PreviousValue = 4_000,
            CurrentFilerCount = 5,
            PreviousFilerCount = 3,
        };

        var stockLabelType = typeof(HoldingsExportController).GetNestedType(
            "StockLabel",
            BindingFlags.NonPublic
        );
        var stockLabelCtor = stockLabelType.GetConstructor([
            typeof(Guid),
            typeof(string),
            typeof(string),
        ]);
        var stockLabel = stockLabelCtor.Invoke([stockId, "AAPL", "Apple Inc."]);
        var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(Guid), stockLabelType);
        var stocks = (IDictionary)Activator.CreateInstance(dictType);
        stocks.Add(stockId, stockLabel);

        var activityRow = typeof(HoldingsExportController).GetMethod(
            "ActivityRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var result = (string[])
            activityRow.Invoke(
                null,
                ["TopBuys", activity, new DateOnly(2024, 12, 31), new DateOnly(2024, 9, 30), stocks]
            );

        result[7].Should().Be("600");
    }
}
