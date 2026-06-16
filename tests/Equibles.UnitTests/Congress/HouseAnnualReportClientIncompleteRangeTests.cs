using Equibles.Congress.HostedService.Services;
using static Equibles.Congress.HostedService.Services.HouseAnnualReportClient;

namespace Equibles.UnitTests.Congress;

public class HouseAnnualReportClientIncompleteRangeTests
{
    private static List<ScheduleToken> Tokens(params (string text, double left)[] words) =>
        words.Select(w => new ScheduleToken(w.text, w.left)).ToList();

    [Fact]
    public void ParseScheduleLines_RangeUpperBoundNeverArrives_DropsTheRow()
    {
        // Line items must carry "the form's own brackets". A range cell that
        // opens with "$1,000,001 -" whose wrapped upper bound never arrives
        // (truncated document) is not a bracket the filer checked — the row
        // must be dropped, not emitted as a fabricated (0, $1,000,001) range.
        var result = HouseAnnualReportClient.ParseScheduleLines([
            Tokens(("S", 22), ("A:", 92)),
            Tokens(
                ("Asset", 25),
                ("Owner", 241),
                ("Value", 280),
                ("of", 311),
                ("Asset", 323),
                ("Income", 363),
                ("Type(s)", 402),
                ("Income", 445)
            ),
            Tokens(("Truncated", 25), ("Asset", 68), ("SP", 241), ("$1,000,001", 280), ("-", 329)),
        ]);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }
}
