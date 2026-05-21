using System.Reflection;
using Equibles.Holdings.Repositories.Models;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class HoldingsScreenerControllerBuildCsvNullPercentOfFloatTests
{
    private static readonly MethodInfo BuildCsvMethod =
        typeof(HoldingsScreenerController).GetMethod(
            "BuildCsv",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // The ScreenerRow.PercentOfFloat doc-comment is explicit: "the value is
    // null whenever SharesOutStanding == 0 (unknown)". The CSV column for
    // PercentOfFloat must therefore be emitted as an EMPTY cell — never
    // "0.0000" (a real "filers own 0% of float" signal) and never a thrown
    // InvalidOperationException from `.Value` on a null double?. A refactor
    // that dropped the `HasValue` ternary and went straight to
    // `r.PercentOfFloat.Value.ToString("F4", …)` would crash the export
    // for any row backed by an unknown-float stock; one that defaulted to
    // 0 would silently fabricate a "no filers" reading.
    [Fact]
    public void BuildCsv_RowWithNullPercentOfFloat_EmitsEmptyTrailingCellWithoutThrow()
    {
        var rows = new List<ScreenerRow>
        {
            new ScreenerRow
            {
                Ticker = "AAPL",
                Name = "Apple Inc.",
                IndustryName = "Tech",
                CurrentFilerCount = 1,
                PreviousFilerCount = 0,
                CurrentValue = 1,
                PreviousValue = 0,
                NewFilerCount = 0,
                SoldOutFilerCount = 0,
                PercentOfFloat = null,
            },
        };

        var csv = (string)BuildCsvMethod.Invoke(null, [rows]);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2); // header + 1 data row
        var dataCells = lines[1].TrimEnd('\r').Split(',');
        dataCells.Should().HaveCount(12);
        dataCells[11].Should().BeEmpty();
    }
}
