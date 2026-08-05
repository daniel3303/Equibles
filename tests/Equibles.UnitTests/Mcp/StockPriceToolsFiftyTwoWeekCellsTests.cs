using System.Globalization;
using System.Reflection;
using Equibles.Yahoo.Mcp.Tools;

namespace Equibles.UnitTests.Mcp;

// Pins GetLatestPrices' 52-week range cells: the absolute high/low of daily closes over the
// 365 days ending on the row's own session, the percent distance of the close from each
// bound, and the short-history star.
//
// The contract this defends: agent callers asked for the derived range so they don't have to
// haul ~250 daily rows per ticker to compute two numbers. Off High must be zero-or-negative
// and Above Low zero-or-positive by construction (the latest close is inside the window that
// produced the bounds), and a window that begins more than a slack past the year boundary is
// a shorter listed history — its values are starred, never silently presented as a full year.
public class StockPriceToolsFiftyTwoWeekCellsTests
{
    private static MethodInfo Method() =>
        typeof(StockPriceTools).GetMethod(
            "BuildFiftyTwoWeekCells",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    private static (string High, string Low, string OffHigh, string AboveLow, bool Starred) Build(
        decimal close,
        decimal high,
        decimal low,
        DateOnly oldest,
        DateOnly cutoff
    )
    {
        var cells = Method().Invoke(null, [close, high, low, oldest, cutoff]);
        var type = cells.GetType();
        return (
            (string)type.GetProperty("High").GetValue(cells),
            (string)type.GetProperty("Low").GetValue(cells),
            (string)type.GetProperty("OffHigh").GetValue(cells),
            (string)type.GetProperty("AboveLow").GetValue(cells),
            (bool)type.GetProperty("Starred").GetValue(cells)
        );
    }

    private static readonly DateOnly Cutoff = new(2025, 8, 4);

    [Fact]
    public void ExposesThePrivateStaticHelper()
    {
        // Guards the reflection lookup itself: a rename would otherwise NRE rather than
        // reporting that the pinned helper is gone.
        Method().Should().NotBeNull();
    }

    [Fact]
    public void FullYearWindow_RendersBoundsAndPercentDistances()
    {
        var cells = Build(90m, 120m, 60m, Cutoff, Cutoff);

        cells.High.Should().Be("120.00");
        cells.Low.Should().Be("60.00");
        cells.OffHigh.Should().Be("-25.00%");
        cells.AboveLow.Should().Be("+50.00%");
        cells.Starred.Should().BeFalse();
    }

    [Fact]
    public void CloseAtTheHigh_ReportsZeroOffHigh()
    {
        var cells = Build(120m, 120m, 60m, Cutoff, Cutoff);

        cells.OffHigh.Should().Be("0.00%");
        cells.AboveLow.Should().Be("+100.00%");
    }

    [Fact]
    public void WindowStartingWithinTheSlack_IsNotStarred()
    {
        // Markets close for weekends and holidays, so a window whose first bar sits a few
        // days past the exact year boundary is still a full year of listed history.
        var cells = Build(90m, 120m, 60m, Cutoff.AddDays(14), Cutoff);

        cells.Starred.Should().BeFalse();
        cells.High.Should().Be("120.00");
    }

    [Fact]
    public void ShorterListedHistory_StarsTheBoundsButKeepsTheDistances()
    {
        var cells = Build(90m, 120m, 60m, Cutoff.AddDays(15), Cutoff);

        cells.Starred.Should().BeTrue();
        cells.High.Should().Be("120.00\\*");
        cells.Low.Should().Be("60.00\\*");
        cells.OffHigh.Should().Be("-25.00%");
        cells.AboveLow.Should().Be("+50.00%");
    }

    [Fact]
    public void NonPositiveBounds_BlankTheDistancesNotTheRow()
    {
        // A corrupted zero bound can't produce a division, but the row itself still renders.
        var cells = Build(90m, 0m, 0m, Cutoff, Cutoff);

        cells.OffHigh.Should().Be("—");
        cells.AboveLow.Should().Be("—");
    }

    [Fact]
    public void NonPositiveClose_BlanksTheDistances()
    {
        // A corrupt $0 row close would otherwise render both distances as -100%, breaking the
        // ≤0 / ≥0 sign contract; the absolute bounds still render.
        var cells = Build(0m, 120m, 60m, Cutoff, Cutoff);

        cells.High.Should().Be("120.00");
        cells.Low.Should().Be("60.00");
        cells.OffHigh.Should().Be("—");
        cells.AboveLow.Should().Be("—");
    }

    [Fact]
    public void PlaceholderRow_MatchesTheWidenedColumnCount()
    {
        // The placeholder must always carry the same cell count as the 10-column header, or a
        // ticker fallback renders a broken markdown table with no other signal.
        var method = typeof(StockPriceTools).GetMethod(
            "PlaceholderRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var row = (string)method.Invoke(null, ["ZZZZ", "No data"]);

        row.Count(c => c == '|').Should().Be(11);
    }

    [Fact]
    public void PercentFormatting_IsCultureInvariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var cells = Build(90m, 120m, 60m, Cutoff, Cutoff);

            cells.OffHigh.Should().Be("-25.00%");
            cells.High.Should().Be("120.00");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
