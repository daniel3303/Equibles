using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Web.Controllers;
using Equibles.Web.ViewModels.Holdings;

namespace Equibles.UnitTests.Web;

public class HoldingsActivityControllerBuildHeatMapPointScoreTests
{
    // BuildHeatMapPoint (single-quarter overload, freshly extracted in #3433) scores a
    // stock from three component percentages. Oracle derived from the documented contract,
    // not the body: net conviction = net filer change / current (20), retention = share of
    // PRIOR filers NOT sold out = 70/80 = 87.5%, universe share = current / total (10), and
    // ConvictionScore is their sum (117.5). Retention is the risky term — it divides by the
    // PREVIOUS count, not the current one, and is the only component bypassing Percentage.Of.
    [Fact]
    public void BuildHeatMapPoint_SingleQuarter_ScoresComponentsAndSumsThem()
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
            CurrentFilerCount = 100,
            PreviousFilerCount = 80,
            NewFilerCount = 30,
            SoldOutFilerCount = 10,
            CurrentValue = 1_000_000,
        };

        var point = (HeatMapPoint)method!.Invoke(null, [activity, 1000, stocks]);

        point.NetConvictionPct.Should().Be(20.0);
        point.RetentionPct.Should().Be(87.5);
        point.UniversePct.Should().Be(10.0);
        point.ConvictionScore.Should().Be(117.5);
    }
}
