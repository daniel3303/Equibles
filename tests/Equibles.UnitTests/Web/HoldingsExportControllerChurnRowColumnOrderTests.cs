using System.Collections;
using System.Reflection;
using Equibles.Holdings.Repositories.Models;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class HoldingsExportControllerChurnRowColumnOrderTests
{
    // The Activity CSV header declares: ..., NewFilerCount(col 9), SoldOutFilerCount(col 10).
    // ChurnRow's last two emit calls populate exactly those columns; a refactor that
    // swaps the order of `Format(NewFilerCount)` and `Format(SoldOutFilerCount)` — easy
    // to do, since both are `(long)`-cast `int` properties — would silently mislabel
    // every "new positions" / "sold-out positions" board in the analyst export.
    // Pin distinct values so the swap is observably wrong on column 9 alone.
    [Fact]
    public void ChurnRow_PopulatesNewFilerCountInColumn9AndSoldOutFilerCountInColumn10()
    {
        var stockId = Guid.NewGuid();
        var churn = new MarketWideStockChurn
        {
            CommonStockId = stockId,
            NewFilerCount = 42,
            SoldOutFilerCount = 99,
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

        var churnRow = typeof(HoldingsExportController).GetMethod(
            "ChurnRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var result = (string[])
            churnRow.Invoke(
                null,
                [
                    "NewPositions",
                    churn,
                    new DateOnly(2024, 12, 31),
                    new DateOnly(2024, 9, 30),
                    stocks,
                ]
            );

        result[9].Should().Be("42");
        result[10].Should().Be("99");
    }
}
