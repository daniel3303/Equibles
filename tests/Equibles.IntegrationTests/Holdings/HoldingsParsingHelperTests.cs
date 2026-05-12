using Equibles.Holdings.HostedService.Models;
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
    public void ParseInvestmentDiscretion_OtrAbbreviation_ReturnsOther() {
        // 13F INFOTABLE.tsv encodes "Other" discretion as the four-character code OTR — not
        // OTH, OTHR, or OTHER as a developer reading SEC's plain-English column header might
        // guess. The DFND branch is already pinned by the sibling [Fact], but OTR has the
        // same regression risk: a refactor that "fixes" what looks like a truncated
        // abbreviation (e.g. someone changes the literal to "OTHER") would silently drop
        // every Other-discretion row through the `_ => Sole` fallback, mis-categorising the
        // 13F manager's discretion mix. Pin OTR→Other so a literal-string drift is caught.
        var result = HoldingsParsingHelper.ParseInvestmentDiscretion("OTR");

        result.Should().Be(Equibles.Holdings.Data.Models.InvestmentDiscretion.Other);
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

    [Fact]
    public void ResolveManagerName_AccessionAndSequenceFoundInOtherManagers_ReturnsMappedName() {
        // 13F-HR filings can carry multiple managers per accession — the primary filing
        // manager from SUBMISSION.tsv plus zero-or-more "other managers" listed in
        // OTHERMANAGER2.tsv keyed by `(ACCESSION_NUMBER, SEQUENCENUMBER)`. During INFOTABLE
        // processing, each holding row points to a manager via its OTHERMANAGER index;
        // ResolveManagerName walks the nested `OtherManagers[accession][seq]` dictionary to
        // turn that index back into a human-readable name. A regression that lost the inner
        // dictionary or keyed by the wrong tuple would leave every multi-manager holding
        // attributed to "null" — silently flattening real co-advisor relationships.
        //
        // This `[Fact]` pins the happy-path lookup at minimum size: one accession with two
        // sequence-numbered managers, asks for sequence 2, asserts the second name comes
        // back. Covers (a) the outer dictionary hit, (b) the inner dictionary hit, (c) the
        // exact sequence-number selection (not e.g. first-in-iteration order).
        var context = new ImportContext {
            OtherManagers = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase) {
                ["ACC-001"] = new() {
                    [1] = "Capital Group Companies, Inc.",
                    [2] = "Capital Research Global Investors",
                },
            },
        };

        var result = HoldingsParsingHelper.ResolveManagerName(context, "ACC-001", managerNumber: 2);

        result.Should().Be("Capital Research Global Investors");
    }

    [Fact]
    public void ParseShareType_PrnAbbreviation_ReturnsPrincipal() {
        // 13F INFOTABLE.tsv encodes the share-quantity unit in column SSHPRNAMTTYPE with
        // exactly two valid values: `SH` (equity share count) and `PRN` (principal amount —
        // bond face value). PRN is the surprising one — a developer expanding the
        // abbreviation by guess is more likely to read it as `Principal` (✓) than as
        // `Print`, `Premium`, or anything else, but the real risk is a refactor that
        // accidentally types `Default` or simply drops the branch (causing every bond
        // holding to be classified as `Shares`, which then double-counts when totals are
        // computed as `Shares × price` rather than `Principal × price-per-100`). Pinning
        // PRN→Principal here protects every bond holding in 13F filings from silently
        // falling through to the share-count default. Production reads the value through
        // `ToUpperInvariant()` first, so the assertion uses uppercase to match the SEC TSV
        // wire format.
        var result = HoldingsParsingHelper.ParseShareType("PRN");

        result.Should().Be(Equibles.Holdings.Data.Models.ShareType.Principal);
    }

    [Fact]
    public void ParseOptionType_CallString_ReturnsCall() {
        // 13F INFOTABLE.tsv column PUTCALL distinguishes derivative holdings: blank for
        // direct equity, `PUT` for put options, `CALL` for call options. The parser returns
        // `OptionType?` — null for the blank/unknown case, distinct enum values for the
        // two named cases. This `[Fact]` pins the `CALL → Call` branch.
        //
        // The risk pattern here is the silent-fallthrough: `_ => null` swallows any value
        // the parser doesn't recognise, including hypothetical case-typos like `Call`
        // (mixed case) IF the production code ever loses its `ToUpperInvariant()` upstream.
        // The assertion uses uppercase `CALL` to match the wire format, and a non-null
        // expectation discriminates against the swallow path. Together with the existing
        // `[Fact]` set, this completes coverage of every named branch in
        // `HoldingsParsingHelper`.
        var result = HoldingsParsingHelper.ParseOptionType("CALL");

        result.Should().Be(Equibles.Holdings.Data.Models.OptionType.Call);
    }
}
