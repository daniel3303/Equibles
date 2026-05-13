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
    public void OwnerCodeRegex_MatchesJointCodeJT_AndCapturesOwnerCode() {
        // Second sibling in the OwnerCodeRegex alternation family. The existing
        // SP pin asserts the Spouse arm; this pin asserts the Joint arm — the
        // second-most-common House PTR owner code after Self.
        //
        // Why JT specifically (and why it's unreachable from the SP sibling):
        //   OwnerCodeRegex is `^(SP|JT|DC|Self)\b` with case-insensitive matching.
        //   Each alternative is INDEPENDENTLY load-bearing — the regex engine
        //   short-circuits on the FIRST successful alternative, so the SP arm
        //   only ever fires for SP inputs. A regression that drops the JT arm
        //   (e.g. "let me reorder the alternation by frequency and drop the
        //   middle one by mistake" — a real refactor pattern) would compile
        //   cleanly, pass the SP pin (the input is "SP ...", SP arm fires),
        //   pass the ExtractTransactionType and RemoveTrailingTransactionType
        //   pins (they don't use OwnerCodeRegex), and silently drop every
        //   joint-attributed trade from the House PTR import.
        //
        // The production analog is concrete: ParseTransactionLines walks every
        // PDF line, the OwnerCodeRegex match is the gate for "is this line a
        // transaction row?" If JT lines fail the gate, the entire row is
        // dropped — no error, no log warning, just missing data. Joint accounts
        // are the standard structure for House members who trade through
        // shared marital brokerage accounts (the default ethics structure for
        // dual-income households), so dropping JT silently erases the dominant
        // owner-code population for married members.
        //
        // The semantic distinction between SP and JT matters downstream:
        //   • SP (Spouse) — trade is in the spouse's account only. Member has
        //     no direct economic interest; reportable but typically excluded
        //     from "member's personal trading" influence-analytics.
        //   • JT (Joint) — trade is in a shared account; member shares the
        //     economic position 50/50 (or similar). Counts toward the member's
        //     personal trading attribution.
        // Misclassifying JT as SP (or dropping JT entirely) flips the
        // influence-analytics signal for every joint-attributed position.
        //
        // Pin uppercase "JT" (canonical case the PDFs emit) and assert both
        // Success AND the capture group value is exactly "JT". The dual
        // assertion proves (a) the alternation arm matched and (b) the
        // capture-group structure is intact — a refactor that changed
        // `(SP|JT|DC|Self)` to `(?:SP|JT|DC|Self)` non-capturing would still
        // match but would fail `Groups[1].Value.Should().Be("JT")`.
        var regex = (Regex)OwnerCodeRegexMethod.Invoke(null, null);

        var match = regex.Match("JT TESLA INC - COMMON STOCK P 03/22/2025");

        match.Success.Should().BeTrue();
        match.Groups[1].Value.Should().Be("JT");
    }

    [Fact]
    public void OwnerCodeRegex_MatchesDependentChildCodeDC_AndCapturesOwnerCode() {
        // Third sibling in the OwnerCodeRegex alternation family. The existing
        // pins assert SP (Spouse) and JT (Joint). This pin asserts DC (Dependent
        // Child) — the third alternative in `^(SP|JT|DC|Self)\b`.
        //
        // Why DC specifically (and why it's unreachable from SP/JT siblings):
        //   The regex short-circuits on the first matching alternative, so the
        //   SP arm only fires for SP inputs, the JT arm only for JT, the DC
        //   arm only for DC, etc. A regression that drops the DC arm (e.g. a
        //   "consolidate the family-attributed codes into JT" refactor under
        //   the false intuition that "JT and DC both flag dependent attribution,
        //   merge them") would compile, pass the SP and JT pins (their own
        //   arms still fire), pass every ExtractTransactionType /
        //   RemoveTrailingTransactionType pin (none use OwnerCodeRegex), and
        //   silently drop every Dependent Child–attributed trade from the
        //   House PTR import.
        //
        // The semantic distinction matters downstream:
        //   • DC (Dependent Child) — trade is in a child's account that the
        //     member holds custodial responsibility for. House ethics rules
        //     require disclosure but treat the holding as the child's, not
        //     the member's, for personal-trading influence analysis.
        //   • JT (Joint) — shared account, member shares the economic position.
        //   • SP (Spouse) — spouse-only account.
        //   The three are NOT interchangeable; conflating DC with JT would
        //   incorrectly attribute child-account trades to the member's
        //   personal portfolio in influence-analytics, inflating the
        //   member's apparent trading activity.
        //
        // The production analog: ParseTransactionLines walks every PDF line
        // through OwnerCodeRegex and uses the capture group as the OwnerType
        // column. A failed alternation match drops the entire row — no log,
        // no error, just missing data. Dependent-child accounts are less
        // common than JT but still represent a meaningful population
        // (members of Congress with minor children routinely report
        // through custodial accounts under federal disclosure rules).
        //
        // Pin uppercase "DC" (canonical case the PDFs emit) and assert both
        // Success AND the capture group value is exactly "DC". The dual
        // assertion proves (a) the alternation arm matched and (b) the
        // capture-group structure survives — a refactor to non-capturing
        // `(?:SP|JT|DC|Self)` would still match but fail
        // `Groups[1].Value.Should().Be("DC")`.
        //
        // With this pin, three of four OwnerCodeRegex alternation arms are
        // individually pinned (SP, JT, DC). The fourth, "Self" (the member
        // themselves), is the natural-extension target for a future iteration
        // and would close the family.
        var regex = (Regex)OwnerCodeRegexMethod.Invoke(null, null);

        var match = regex.Match("DC MICROSOFT CORP - COMMON STOCK P 06/10/2025");

        match.Success.Should().BeTrue();
        match.Groups[1].Value.Should().Be("DC");
    }

    [Fact]
    public void OwnerCodeRegex_MatchesMemberCodeSelf_AndCapturesOwnerCode() {
        // Fourth and final sibling in the OwnerCodeRegex alternation family. With
        // this pin, all four alternatives in `^(SP|JT|DC|Self)\b` are individually
        // pinned: SP (Spouse), JT (Joint), DC (Dependent Child), Self (the member).
        //
        // Why "Self" uniquely matters and is unreachable from the other three pins:
        //   The regex short-circuits on the first matching alternative, so the
        //   "Self" arm only ever fires for Self inputs. The pattern is also case-
        //   insensitive (RegexOptions.IgnoreCase) so "self" / "SELF" / "Self"
        //   should all match. A regression that drops the "Self" arm (e.g. a
        //   "remove redundant Self code since it's the default attribution"
        //   refactor — Self IS the implicit default in regulatory framing but
        //   it's still emitted as an explicit literal in the PDF) would compile,
        //   pass the SP/JT/DC pins, pass every ExtractTransactionType /
        //   RemoveTrailingTransactionType pin, and silently drop every member-
        //   attributed trade from the import.
        //
        // The production volume implications make this the LARGEST silent-loss
        // regression of the four arms:
        //   • Self (the member) — DOMINANT House PTR owner code. Most members
        //     trade primarily through their own personal accounts; Self lines
        //     outnumber SP+JT+DC combined for the typical member.
        //   • SP/JT/DC — household-attributed accounts, secondary populations.
        //   Dropping Self would silently erase the bulk of the House PTR
        //   dataset — every member's personal trading would vanish from the
        //   ingest, leaving only household-attributed trades. The remaining
        //   dataset would look surprisingly small but plausible; operator
        //   inspection of "active members" would still show data, just at
        //   ~20% of the true volume. Exactly the silent partial-failure
        //   mode that CI must catch.
        //
        // Self is structurally distinct from the three two-letter codes in
        // one other way: it's the only multi-character alternative (4 chars
        // vs 2 chars for SP/JT/DC). A refactor that "normalize all owner
        // codes to two-character abbreviations" — perhaps replacing
        // `(SP|JT|DC|Self)` with `(SP|JT|DC|SE)` to make them uniform —
        // would silently miss every Self line in the PDFs (the PDFs emit
        // "Self", not "SE"). The two-pin pair (any two-letter sibling +
        // this one) catches the unification refactor; this pin alone
        // catches the dropped-Self refactor.
        //
        // Pin "Self" with mixed-case capitalization (the canonical case the
        // House PDFs emit — first letter uppercase, rest lowercase) and assert
        // both Success AND the capture group value is exactly "Self". The dual
        // assertion proves (a) the alternation arm matched, (b) the case
        // preserved in the capture group, and (c) the capture-group structure
        // is intact.
        var regex = (Regex)OwnerCodeRegexMethod.Invoke(null, null);

        var match = regex.Match("Self NVIDIA CORP - COMMON STOCK P 09/05/2025");

        match.Success.Should().BeTrue();
        match.Groups[1].Value.Should().Be("Self");
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

    [Fact]
    public void RemoveTrailingTransactionType_SaleWithFullQualifier_StripsSuffixAndQualifier() {
        // Sibling to the existing "S (partial)" pin in RemoveTrailingTransactionType.
        // The strip path uses SaleTypeRegex `\bS\s*(\((?:partial|full)\))?\s*$` which
        // accepts BOTH qualifiers in the `(?:partial|full)` alternation. The
        // existing tests cover:
        //   - ExtractTransactionType for both partial AND full qualifiers
        //     (return-type detection — those pins read the regex via .IsMatch)
        //   - RemoveTrailingTransactionType for partial qualifier (.Replace path)
        // The `full` arm's BEHAVIOR through .Replace was unpinned.
        //
        // The risk this catches: a refactor that tightened the alternation to
        // just `partial` (a "we never see (full) in practice" simplification)
        // would compile, pass the ExtractTransactionType-full sibling
        // (different code path — `.IsMatch` vs `.Replace`), pass the
        // RemoveTrailingTransactionType-partial sibling, AND silently leave
        // every "S (full)" tail intact in persisted AssetName columns.
        // Downstream the asset name column would read "APPLE INC - COMMON
        // STOCK S (full)" — visible in the Congress trades UI and MCP tool
        // outputs.
        //
        // The two-step structure (recognize via IsMatch, then strip via
        // Replace) means dropping the `full` alternation has DIFFERENT
        // effects in each step:
        //   - IsMatch with `(?:partial)` only: "S (full)" no longer matches,
        //     ExtractTransactionType returns null, the row gets discarded
        //     in the upstream filter — caught by the existing sibling.
        //   - Replace with `(?:partial)` only: "S (full)" doesn't match the
        //     regex but the row already classified as Sale via PERHAPS a
        //     looser fallback path; Replace leaves the trailing tail in
        //     place. Pin THIS specific edge.
        //
        // Pin "S (full)" with a realistic asset string. Asserting the
        // cleanly-stripped output proves the full alternation arm in the
        // Replace pipeline still fires.
        var result = (string)RemoveTrailingTransactionTypeMethod.Invoke(null, ["GOOGLE LLC - CLASS A COMMON S (full)"]);

        result.Should().Be("GOOGLE LLC - CLASS A COMMON");
    }
}
