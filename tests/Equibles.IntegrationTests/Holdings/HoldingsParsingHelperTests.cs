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

    [Fact]
    public void ParseInvestmentDiscretion_DfndAbbreviation_ReturnsDefined() {
        // 13F INFOTABLE.tsv encodes the discretion column as a four-character SEC
        // abbreviation: SOLE / DFND / OTR. The first two map 1:1 (`Sole`, `Other`) but `DFND`
        // is the surprising one — it expands to `Defined`, not `Default` (a developer
        // refactoring the parser by autocomplete is far more likely to type `Default`).
        // Pinning this here means a rename that flips the mapping silently — every 13F row
        // with `INVESTMENTDISCRETION=DFND` would land on the fallback (`Sole`) and the
        // entire defined-discretion fleet would misrepresent — fails loudly instead. The
        // production reading runs through `value?.ToUpperInvariant()` first, so the
        // assertion uses uppercase to match what real SEC TSVs ship.
        var result = HoldingsParsingHelper.ParseInvestmentDiscretion("DFND");

        result.Should().Be(Equibles.Holdings.Data.Models.InvestmentDiscretion.Defined);
    }
}
