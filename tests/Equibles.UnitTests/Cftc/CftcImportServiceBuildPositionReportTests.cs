using System.Reflection;
using Equibles.Cftc.Data.Models;
using Equibles.Cftc.HostedService.Services;
using Equibles.Integrations.Cftc.Models;

namespace Equibles.UnitTests.Cftc;

/// <summary>
/// Sibling to <see cref="CftcImportServiceTests"/>. That pins <c>ParseDate</c>;
/// this pins the newly-extracted private static <c>BuildPositionReport</c>
/// helper (#1396). The mapping has two distinct null conventions for the
/// nullable upstream <see cref="CftcReportRecord"/> fields:
///
/// • <b>Main position counts</b> (OpenInterest, NonCommLong, CommShort, …) are
///   <c>long</c> on the entity and default to <c>0</c> via <c>?? 0</c>.
/// • <b>Change / Pct / Traders fields</b> are nullable on the entity and pass
///   through as <c>record.X</c> with no default — null in stays null out, so
///   the DB distinguishes "we don't have this number" from "this number is 0."
///
/// A regression that dropped <c>?? 0</c> from a main field, or added one to a
/// nullable field, would silently flip those semantics. Feed a record with
/// both legs null and pin both outcomes from the same input.
/// </summary>
public class CftcImportServiceBuildPositionReportTests
{
    private static readonly MethodInfo BuildPositionReportMethod =
        typeof(CftcImportService).GetMethod(
            "BuildPositionReport",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    [Fact]
    public void BuildPositionReport_AllNullableUpstreamFields_ZeroDefaultsCoreFieldsPreservesNullChangeFields()
    {
        var record = new CftcReportRecord
        {
            OpenInterest = null,
            ChangeOpenInterest = null,
            PctCommLong = null,
            TradersTotal = null,
        };
        var contractId = Guid.NewGuid();
        var reportDate = new DateOnly(2026, 3, 15);

        var result = (CftcPositionReport)
            BuildPositionReportMethod.Invoke(null, [record, contractId, reportDate]);

        // Main position field: zero-defaulted (the field is non-nullable on the entity).
        result.OpenInterest.Should().Be(0);
        // Nullable Change / Pct / Traders fields: null preserved (no `?? 0`).
        result.ChangeOpenInterest.Should().BeNull();
        result.PctCommLong.Should().BeNull();
        result.TradersTotal.Should().BeNull();
    }
}
