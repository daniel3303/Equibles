using System.Reflection;
using System.Text.RegularExpressions;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Tests for <see cref="HouseDisclosureClient"/>. The public entry points pull PDFs from the
/// House Clerk site, so we exercise the pure-logic private regex helpers via reflection.
/// </summary>
public class HouseDisclosureClientTests {
    private static readonly MethodInfo ExtractTransactionTypeMethod = typeof(HouseDisclosureClient)
        .GetMethod("ExtractTransactionType", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo RemoveTrailingTransactionTypeMethod = typeof(HouseDisclosureClient)
        .GetMethod("RemoveTrailingTransactionType", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo OwnerCodeRegexMethod = typeof(HouseDisclosureClient)
        .GetMethod("OwnerCodeRegex", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo DatePatternRegexMethod = typeof(HouseDisclosureClient)
        .GetMethod("DatePatternRegex", BindingFlags.NonPublic | BindingFlags.Static);

    [Fact]
    public void DatePatternRegex_MatchesMmDdYyyyDateInPdfLineText() {
        // DatePatternRegex pattern: `\b(\d{2}/\d{2}/\d{4})\b`. Used by
        // ParseTransactionLines to locate the transaction date within a PDF
        // text line that mixes owner code, asset description, transaction
        // type marker, and date — e.g. "SP APPLE INC - COMMON STOCK P 01/14/2025".
        // Without a match the line is skipped (`if (!dateMatch.Success) continue;`);
        // with a match, the captured date string flows into ParseDate to become
        // the persisted TransactionDate.
        //
        // The risk: a refactor that drops the `\b` word boundaries (e.g. "tidy
        // up" the pattern to `(\d{2}/\d{2}/\d{4})`) would silently match
        // date-looking substrings inside longer numeric runs — `1234/56/789012`
        // would produce a phantom "34/56/7890" capture. ParseDate would reject
        // most such captures, but borderline cases could produce wrong dates
        // that downstream date-window analytics ("trades within N days of a
        // hearing") would silently mis-bucket.
        //
        // Sibling to OwnerCodeRegex_MatchesSpouseCodeSP. Same reflection pattern
        // (private static partial Regex via GeneratedRegex). Assert both
        // Success AND the captured date string so a regression that broke the
        // capture group (e.g. `(?:...)` non-capturing) also fails.
        var regex = (Regex)DatePatternRegexMethod.Invoke(null, null);

        var match = regex.Match("SP APPLE INC - COMMON STOCK P 01/14/2025 $1,001 - $15,000");

        match.Success.Should().BeTrue();
        match.Groups[1].Value.Should().Be("01/14/2025");
    }

    [Fact]
    public void OwnerCodeRegex_MatchesSpouseCodeSP_AndCapturesOwnerCode() {
        // OwnerCodeRegex pattern: `^(SP|JT|DC|Self)\b` (case-insensitive).
        // The four alternatives are the four House PTR owner codes:
        //   • SP   — Spouse
        //   • JT   — Joint (member + spouse)
        //   • DC   — Dependent Child
        //   • Self — the member
        // Each is independently load-bearing: ParseTransactionLines walks every PDF
        // line through this regex, skips lines that don't match, and uses the
        // capture group as the OwnerType field. A regression that drops any single
        // alternative (e.g. "let me alphabetize the alternation" reorder that
        // typos one out, or a "consolidate to SP|JT" simplification) would silently
        // drop every PTR row attributed to that owner code from the import.
        //
        // The existing pins in this file cover ExtractTransactionType and
        // RemoveTrailingTransactionType but NOT OwnerCodeRegex. Spouse (SP) is
        // the highest-volume non-Self owner code in the House PTR corpus
        // (members frequently trade through joint or spouse-attributed accounts
        // for ethics-reporting reasons). Pin SP specifically so a regression that
        // drops the SP arm of the alternation surfaces here rather than as
        // silently-missing spouse-attributed trades in the dashboard.
        //
        // Two assertions: the regex matched (Success) AND the captured group
        // value is exactly "SP" (so a regression that broke the capture group
        // structure — e.g. changing `(SP|...)` to `(?:SP|...)` non-capturing —
        // would also fail). The downstream `ownerMatch.Groups[1].Value` flows
        // straight into the persisted OwnerType column.
        var regex = (Regex)OwnerCodeRegexMethod.Invoke(null, null);

        var match = regex.Match("SP APPLE INC - COMMON STOCK P 01/14/2025");

        match.Success.Should().BeTrue();
        match.Groups[1].Value.Should().Be("SP");
    }

    [Fact]
    public void ExtractTransactionType_PurchaseTrailingP_ReturnsPurchase() {
        // House PTRs encode purchases as a trailing bare "P" (no qualifier). The
        // PurchaseTypeRegex (`\bP\s*$`) is the only matcher for that path — if a
        // regression typo'd it to `^P\b` (anchored to start) or dropped the
        // word-boundary, every Purchase transaction would silently classify as null
        // and be dropped by the ProcessRow loop in CongressionalTradeSyncService.
        // The companion Sale [Fact] covers the more complex SaleTypeRegex; this one
        // pins the simpler Purchase branch — equally load-bearing, currently
        // unpinned, and trivial to regress because the regex is one line away.
        var result = (CongressTransactionType?)ExtractTransactionTypeMethod.Invoke(null, ["AAPL P"]);

        result.Should().Be(CongressTransactionType.Purchase);
    }

    [Fact]
    public void ExtractTransactionType_LowercaseSaleS_StillReturnsSaleViaIgnoreCase() {
        // SaleTypeRegex is declared with `RegexOptions.IgnoreCase` — PurchaseTypeRegex
        // is NOT. The asymmetry is deliberate (House PTRs always emit uppercase "P"
        // and "S" so case-insensitivity has no production value for either, but the
        // SaleTypeRegex's IgnoreCase was added defensively when the (partial)/(full)
        // parenthetical groups were introduced, and PurchaseTypeRegex stayed strict).
        //
        // The risk this pin catches: a refactor that "harmonizes" the two regexes
        // by either DROPPING IgnoreCase from SaleTypeRegex (under the assumption
        // that PurchaseTypeRegex's case-sensitivity is the convention) or ADDING
        // IgnoreCase to PurchaseTypeRegex (consistency change). The dropping case
        // is the dangerous one: lowercase "s" rows in any future aggregator-emitted
        // PTR (e.g., a third-party scraper that normalizes casing before re-publishing)
        // would silently classify as null and be dropped from the import.
        //
        // No other test in this file exercises the IgnoreCase modifier directly —
        // every existing sibling uses uppercase letters. Pin lowercase "s" so the
        // modifier can't be removed silently. The companion bare-S pin already
        // proves uppercase "S" matches; this pin proves the case-insensitive
        // alternative also matches.
        var result = (CongressTransactionType?)ExtractTransactionTypeMethod.Invoke(null, ["AAPL s"]);

        result.Should().Be(CongressTransactionType.Sale);
    }

    [Fact]
    public void ExtractTransactionType_SaleWithPartialQualifier_ReturnsSale() {
        // House PTRs encode partial sales as "S (partial)" rather than bare "S".
        // The SaleTypeRegex must accept both forms — if a regression tightens it to
        // require the parenthetical (or, conversely, drops the optional group),
        // half the House sale transactions get classified as null and are silently
        // dropped by the importer. Pin the qualified-form match so the regex can't
        // narrow without a test failure.
        var result = (CongressTransactionType?)ExtractTransactionTypeMethod.Invoke(null, ["AAPL S (partial)"]);

        result.Should().Be(CongressTransactionType.Sale);
    }

    [Fact]
    public void ExtractTransactionType_SaleWithFullQualifier_ReturnsSale() {
        // Sibling to the existing "S (partial)" pin. The SaleTypeRegex alternation
        // `(?:partial|full)` covers BOTH qualifier forms — a refactor that simplifies
        // the group to just `partial` (e.g. someone "cleaning up" what looks like an
        // unused alternation arm, or copying the pattern incorrectly into a sibling
        // regex) would still pass the existing partial-qualifier test but silently
        // drop every "S (full)" sale transaction. House PTRs use BOTH forms in
        // production: "(full)" appears whenever a member liquidates an entire
        // position, "(partial)" for fractional sells. The two qualifier arms are
        // independent — partial-test coverage does NOT prove full-test coverage.
        // Without this pin, a regex that only accepted one arm would compile cleanly,
        // pass the existing tests, and silently halve House sale visibility while
        // the partial-test would still report a green build.
        var result = (CongressTransactionType?)ExtractTransactionTypeMethod.Invoke(null, ["TSLA S (full)"]);

        result.Should().Be(CongressTransactionType.Sale);
    }

    [Fact]
    public void ExtractTransactionType_BareSaleNoQualifier_ReturnsSale() {
        // Completes the Sale/Purchase pin family. Existing pins cover:
        //   • bare "P"        → Purchase  (ExtractTransactionType_PurchaseTrailingP_…)
        //   • "S (partial)"   → Sale      (existing partial pin)
        //   • "S (full)"      → Sale      (existing full pin)
        // The bare "S" form — `S` at line end with NO parenthetical qualifier — is
        // the most common House PTR sale encoding (used for ordinary whole-position
        // sells where no partial/full qualifier applies), and it's the ONLY form
        // currently unpinned in the family.
        //
        // The SaleTypeRegex pattern is `\bS\s*(\((?:partial|full)\))?\s*$` — the
        // `?` modifier on the parenthetical group is what makes bare S match. A
        // regression that drops the `?` (someone "tightening" the pattern under
        // the false assumption that every Sale must carry a qualifier) would still
        // pass BOTH the partial AND the full sibling pins — those rows have the
        // parenthetical present — while silently classifying every bare-S row as
        // null, dropping them from the import via the `if (txType == null) continue;`
        // guard in ParseTransactionLines. The failure mode is invisible: the import
        // succeeds, just with a fraction of the expected row count, and the
        // dashboard quietly shows fewer insider sales than reality.
        //
        // Pin the bare-S case specifically so the optional-qualifier `?` modifier
        // can't be removed without a test failure.
        var result = (CongressTransactionType?)ExtractTransactionTypeMethod.Invoke(null, ["AAPL S"]);

        result.Should().Be(CongressTransactionType.Sale);
    }

    [Fact]
    public void RemoveTrailingTransactionType_SaleWithPartialQualifier_StripsSuffixAndQualifier() {
        // After ExtractTransactionType recognises the trailing "S (partial)" marker,
        // RemoveTrailingTransactionType has to strip it cleanly so the asset name
        // ("APPLE INC - COMMON STOCK") doesn't carry a dangling " S (partial)" tail
        // into the persisted AssetName column. The two regexes (Sale/Purchase) must
        // be applied in sequence and the result trimmed — without this, downstream
        // consumers (UI, MCP tools) display garbled asset names. Pin the
        // parenthetical-qualifier strip so the regex can't be loosened to a bare
        // "S" matcher that would leave the "(partial)" tail behind.
        var result = (string)RemoveTrailingTransactionTypeMethod.Invoke(null, ["APPLE INC - COMMON STOCK S (partial)"]);

        result.Should().Be("APPLE INC - COMMON STOCK");
    }

    [Fact]
    public void RemoveTrailingTransactionType_PurchaseTrailingP_StripsSuffix() {
        // Sibling to the existing Sale pin. RemoveTrailingTransactionType applies
        // BOTH SaleTypeRegex.Replace AND PurchaseTypeRegex.Replace in sequence —
        // the existing Sale pin only exercises the Sale arm. A regression that
        // drops the PurchaseTypeRegex().Replace() call (plausible during a
        // refactor that consolidates the two replaces into a single combined
        // regex) would slip past the Sale pin entirely: Sale-tagged rows still
        // get stripped, but every Purchase row keeps a dangling " P" tail in
        // the persisted AssetName. Downstream the asset name column reads
        // "APPLE INC - COMMON STOCK P" — visible in the Congress trades UI
        // and MCP tool outputs. This pin exercises the Purchase strip in
        // isolation (no S in the input) so the Purchase replace can't be
        // silently dropped or merged with the Sale replace in a way that
        // changes its semantics. The PurchaseTypeRegex `\bP\s*$` is
        // intentionally case-sensitive (unlike SaleTypeRegex's IgnoreCase),
        // pin asserts the canonical capital-P form rather than mixed case.
        var result = (string)RemoveTrailingTransactionTypeMethod.Invoke(null, ["MICROSOFT CORP - COMMON STOCK P"]);

        result.Should().Be("MICROSOFT CORP - COMMON STOCK");
    }
}
