using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Web.Controllers;
using Equibles.Web.ViewModels.Holdings;

namespace Equibles.UnitTests.Web;

public class HoldingsActivityControllerBuildHeatMapPointZeroPreviousFilersTests
{
    // Retention = share of PRIOR-quarter filers not sold out, dividing by
    // PreviousFilerCount. For a stock with no prior filers there is no base to
    // retain, so the documented guard yields 0 — never a divide-by-zero. The
    // existing score pin only exercises PreviousFilerCount > 0; this pins the
    // == 0 guard, which a dropped check would turn into -Infinity (soldOut/0).
    [Fact]
    public void BuildHeatMapPoint_ZeroPreviousFilersWithSoldOuts_RetentionIsZeroNotNegativeInfinity()
    {
        var controllerType = typeof(HoldingsActivityController);
        var stockLabelType = controllerType.GetNestedType("StockLabel", BindingFlags.NonPublic);
        var iDictType = typeof(IDictionary<,>).MakeGenericType(typeof(Guid), stockLabelType!);
        var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(Guid), stockLabelType!);
        var stocks = Activator.CreateInstance(dictType);

        var method = controllerType.GetMethod(
            "BuildHeatMapPoint",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: [typeof(StockQuarterlyActivity), typeof(int), iDictType],
            modifiers: null
        );

        var activity = new StockQuarterlyActivity
        {
            CommonStockId = Guid.NewGuid(),
            CurrentFilerCount = 10,
            PreviousFilerCount = 0,
            NewFilerCount = 10,
            SoldOutFilerCount = 5,
            CurrentValue = 1_000_000,
        };

        var point = (HeatMapPoint)method!.Invoke(null, [activity, 1000, stocks]);

        point.RetentionPct.Should().Be(0.0);
    }
}
