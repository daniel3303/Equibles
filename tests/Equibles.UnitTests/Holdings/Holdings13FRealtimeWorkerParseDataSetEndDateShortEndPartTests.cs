using Equibles.Holdings.HostedService;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Sibling to ParseDataSetEndDate {InvalidDay, InvalidMonth, YearZero,
/// LeapYearFeb29}Tests. The TryParseDatePart helper has a length-floor guard
/// (`if (part.Length &lt; 9) return null;`) that protects every downstream
/// substring index from IndexOutOfRangeException. The existing pins all feed
/// at-least-9-char endparts, so the floor is unhit — a refactor that dropped
/// the length check (intuitively redundant once `int.TryParse(part[..2])` is
/// in place) would throw IOOR on a malformed SEC bulk-data manifest entry
/// like the unit-format "Q1.zip", aborting the lookback computation and
/// stranding the worker on the default 7-day window. Pin the early-return.
/// </summary>
public class Holdings13FRealtimeWorkerParseDataSetEndDateShortEndPartTests
{
    [Fact]
    public void ParseDataSetEndDate_NewFormatEndPartShorterThanNineChars_ReturnsNullWithoutThrowing()
    {
        // The dash-split splits at the first '-', so this filename's endpart
        // is "Q1" — only 2 chars, well below the 9-char floor in
        // TryParseDatePart. The method must return null, not throw.
        var act = () => Holdings13FRealtimeWorker.ParseDataSetEndDate("2023-Q1_form13f.zip");

        act.Should().NotThrow();
        Holdings13FRealtimeWorker.ParseDataSetEndDate("2023-Q1_form13f.zip").Should().BeNull();
    }
}
