using System.Collections;
using System.Reflection;
using Equibles.Holdings.Repositories.Models;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

/// <summary>
/// ActivityRow computes DeltaValue inline as CurrentValue - PreviousValue
/// (column 8). The sibling test pins DeltaShares (column 7) but DeltaValue
/// is unpinned — a refactor that swaps the subtraction operands on the
/// value column would silently invert every buy/sell dollar delta in the
/// Activity CSV export without failing any existing test.
/// </summary>
public class HoldingsExportControllerActivityRowDeltaValueDirectionTests
{
    [Fact]
    public void ActivityRow_BuyWithCurrentValueGreaterThanPrevious_DeltaValueIsPositive()
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

        // Column 8 = DeltaValue = 10_000 - 4_000 = 6_000
        result[8].Should().Be("6000");
    }
}
