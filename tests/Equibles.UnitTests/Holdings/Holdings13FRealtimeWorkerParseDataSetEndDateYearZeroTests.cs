using Equibles.Holdings.HostedService;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Contract: ParseDataSetEndDate returns null for unparseable file names.
/// Old-format path computes DateTime.DaysInMonth(year, ...) which requires
/// year >= 1 — year 0 throws ArgumentOutOfRangeException instead of returning null.
/// </summary>
public class Holdings13FRealtimeWorkerParseDataSetEndDateYearZeroTests
{
    [Fact]
    public void ParseDataSetEndDate_OldFormatYearZero_ReturnsNullInsteadOfThrowing()
    {
        // Year 0 passes int.TryParse and quarter validation, but
        // DateTime.DaysInMonth(0, ...) throws — violating the null-return contract.
        var act = () => Holdings13FRealtimeWorker.ParseDataSetEndDate("0000q1_form13f.zip");

        act.Should().NotThrow("unparseable dates must return null, not throw");
    }
}
