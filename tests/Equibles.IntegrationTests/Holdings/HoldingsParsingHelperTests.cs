using Equibles.Holdings.HostedService.Services;

namespace Equibles.IntegrationTests.Holdings;

public class HoldingsParsingHelperTests {
    [Fact]
    public void TryParseDateOnly_SecDdMmmYyyyFormat_ParsesCorrectlyAfterIsoLegRejects() {
        // 13F SUBMISSION.tsv routinely emits dates in SEC's classic `dd-MMM-yyyy` form
        // (`30-SEP-2024`) — not the ISO format `DateOnly.TryParse` natively recognises. The
        // parser tries the ISO leg first, falls back to `dd-MMM-yyyy` (invariant culture),
        // and only then gives up. If the fallback ever regresses (e.g. someone removes it
        // thinking ISO is enough, or the invariant-culture parse switches to ordinal-month
        // matching), the entire 13F import silently drops every submission row because
        // PERIODOFREPORT/FILING_DATE parses to `default(DateOnly)`.
        //
        // This `[Fact]` exercises the SEC-format leg specifically with an uppercase month
        // abbreviation — that's what real SEC TSVs ship — and asserts the resulting DateOnly
        // matches the calendar date. Uses September because no other 3-letter month
        // abbreviation collides with it; if the parser accidentally matched on prefix or did
        // something culture-sensitive (e.g. Portuguese "Set"), the test would catch it.

        var success = HoldingsParsingHelper.TryParseDateOnly("30-SEP-2024", out var result);

        success.Should().BeTrue();
        result.Should().Be(new DateOnly(2024, 9, 30));
    }
}
