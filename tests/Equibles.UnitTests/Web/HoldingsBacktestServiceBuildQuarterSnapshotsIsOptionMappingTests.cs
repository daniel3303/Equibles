using System.Collections;
using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories.Models;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class HoldingsBacktestServiceBuildQuarterSnapshotsIsOptionMappingTests
{
    private static readonly Type RowType = typeof(HoldingsBacktestService).GetNestedType(
        "BacktestHoldingRow",
        BindingFlags.NonPublic
    );

    private static readonly MethodInfo BuildQuarterSnapshotsMethod =
        typeof(HoldingsBacktestService).GetMethod(
            "BuildQuarterSnapshots",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // BuildQuarterSnapshots's projection sets `IsOption = h.OptionType != null` —
    // the only place that boolean is computed before the backtest calculator's
    // `Where(p => !p.IsOption && p.Value > 0)` filter runs. A regression that
    // hardcoded IsOption=false (e.g. "stocks only" simplification) would silently
    // let 13F option rows into the cash-equity rebalance, polluting the simulated
    // portfolio with notional option Values as if they were spot positions.
    [Fact]
    public void BuildQuarterSnapshots_RowWithNonNullOptionType_PositionFlaggedAsIsOption()
    {
        var stockId = Guid.NewGuid();
        var listType = typeof(List<>).MakeGenericType(RowType);
        var list = (IList)Activator.CreateInstance(listType);
        list.Add(
            Activator.CreateInstance(
                RowType,
                [new DateOnly(2024, 12, 31), stockId, 1_000L, 500_000L, (OptionType?)OptionType.Put]
            )
        );

        var snapshots =
            (List<BacktestQuarterSnapshot>)BuildQuarterSnapshotsMethod.Invoke(null, [list]);

        var position = snapshots
            .Should()
            .ContainSingle()
            .Subject.Positions.Should()
            .ContainSingle()
            .Subject;
        position.IsOption.Should().BeTrue();
    }
}
