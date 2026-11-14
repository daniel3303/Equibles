using Equibles.Holdings.Data.Models;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class StockTabServiceNewestWholeQuartersTests
{
    private static InstitutionalHolding Row(DateOnly reportDate, OptionType? option = null) =>
        new() { ReportDate = reportDate, OptionType = option };

    // Three lines a quarter and a cap of four: the second quarter would keep one line of three.
    [Fact]
    public void NewestWholeQuarters_CapLandsInsideAQuarter_DropsThatQuarter()
    {
        var latest = new DateOnly(2025, 6, 30);
        var earlier = new DateOnly(2025, 3, 31);
        var history = new List<InstitutionalHolding>
        {
            Row(latest, OptionType.Call),
            Row(latest, OptionType.Put),
            Row(latest),
            Row(earlier, OptionType.Call),
            Row(earlier, OptionType.Put),
            Row(earlier),
        };

        var shown = StockTabService.NewestWholeQuarters(history, maxRows: 4);

        shown.Should().HaveCount(3).And.OnlyContain(h => h.ReportDate == latest);
    }

    [Fact]
    public void NewestWholeQuarters_CapLandsBetweenQuarters_KeepsEveryRowUnderIt()
    {
        var latest = new DateOnly(2025, 6, 30);
        var earlier = new DateOnly(2025, 3, 31);
        var history = new List<InstitutionalHolding>
        {
            Row(latest),
            Row(earlier),
            Row(earlier.AddMonths(-3)),
        };

        var shown = StockTabService.NewestWholeQuarters(history, maxRows: 2);

        shown.Select(h => h.ReportDate).Should().Equal(latest, earlier);
    }
}
