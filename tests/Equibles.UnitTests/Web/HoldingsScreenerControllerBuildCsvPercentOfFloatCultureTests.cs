using System.Globalization;
using System.Reflection;
using Equibles.Holdings.Repositories.Models;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class HoldingsScreenerControllerBuildCsvPercentOfFloatCultureTests
{
    private static readonly MethodInfo BuildCsvMethod =
        typeof(HoldingsScreenerController).GetMethod(
            "BuildCsv",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // BuildCsv renders PercentOfFloat with "F4" + InvariantCulture, so the
    // decimal separator is always a period. A regression that dropped the
    // InvariantCulture argument would render "12,3457" under de-DE — the
    // comma becomes a CSV field separator, splitting the PercentOfFloat
    // column in two and shifting every subsequent row out of alignment.
    [Fact]
    public void BuildCsv_NonNullPercentOfFloat_UsesPeriodDecimalSeparatorUnderCommaCulture()
    {
        var prev = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

            var rows = new List<ScreenerRow>
            {
                new ScreenerRow
                {
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    IndustryName = "Tech",
                    CurrentFilerCount = 10,
                    PreviousFilerCount = 8,
                    CurrentValue = 5_000_000,
                    PreviousValue = 4_000_000,
                    NewFilerCount = 3,
                    SoldOutFilerCount = 1,
                    PercentOfFloat = 12.3456789,
                },
            };

            var csv = (string)BuildCsvMethod.Invoke(null, [rows]);

            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            lines.Should().HaveCount(2);
            var dataCells = lines[1].TrimEnd('\r').Split(',');
            dataCells.Should().HaveCount(12, "a comma in the decimal would produce 13+ cells");
            dataCells[11].Should().Be("12.3457");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prev;
        }
    }
}
