using System.Collections;
using System.Reflection;
using Equibles.Holdings.Repositories.Models;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class HoldingsBacktestServiceBuildQuarterSnapshotsChronologicalOrderTests
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

    // BuildQuarterSnapshots (extracted in #1568) projects a flat list of
    // per-row holdings into per-quarter snapshots. The simulation downstream
    // walks the result expecting chronological order — a refactor that
    // dropped the OrderBy(g => g.Key) would silently emit snapshots in
    // input order, which is non-deterministic at the DB query layer.
    [Fact]
    public void BuildQuarterSnapshots_RowsInReverseChronologicalOrder_OutputAscendingByReportDate()
    {
        var stockId = Guid.NewGuid();
        var holdings = BuildHoldingsList(
            (new DateOnly(2024, 9, 30), stockId, 100L, 50_000L, null),
            (new DateOnly(2024, 6, 30), stockId, 50L, 25_000L, null)
        );

        var snapshots =
            (List<BacktestQuarterSnapshot>)BuildQuarterSnapshotsMethod.Invoke(null, [holdings]);

        snapshots
            .Select(s => s.ReportDate)
            .Should()
            .Equal(new DateOnly(2024, 6, 30), new DateOnly(2024, 9, 30));
    }

    private static object BuildHoldingsList(
        params (
            DateOnly ReportDate,
            Guid StockId,
            long Shares,
            long Value,
            object OptionType
        )[] rows
    )
    {
        var listType = typeof(List<>).MakeGenericType(RowType);
        var list = (IList)Activator.CreateInstance(listType);
        foreach (var r in rows)
        {
            var instance = Activator.CreateInstance(
                RowType,
                [r.ReportDate, r.StockId, r.Shares, r.Value, r.OptionType]
            );
            list.Add(instance);
        }
        return list;
    }
}
