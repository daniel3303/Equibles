using System.Reflection;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorTests {
    private static readonly MethodInfo SanitizeXmlMethod = typeof(InsiderTradingFilingProcessor)
        .GetMethod("SanitizeXml", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo ParseLongMethod = typeof(InsiderTradingFilingProcessor)
        .GetMethod("ParseLong", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo ParseTransactionCodeMethod = typeof(InsiderTradingFilingProcessor)
        .GetMethod("ParseTransactionCode", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo ParseBoolMethod = typeof(InsiderTradingFilingProcessor)
        .GetMethod("ParseBool", BindingFlags.NonPublic | BindingFlags.Static);

    [Fact]
    public void ParseBool_DigitOne_ReturnsTrue() {
        // ParseBool is the four-arm pattern matcher
        //   `value is "1" or "true" or "True" or "TRUE"`
        // used to interpret SEC Form 4 XML elements isDirector, isOfficer, and
        // isTenPercentOwner. The XML schema (SEC's Form 3/4/5 X-rays) emits these
        // flags inconsistently across filers — some serialize them as "1"/"0"
        // (the most common form for `<isDirector>1</isDirector>`), others as
        // "true"/"false". Each helps drive a different downstream consumer:
        //   • isDirector → identifies board members in "directors buying" dashboards
        //   • isOfficer → drives the "officer purchase cluster" alerting pipeline
        //   • isTenPercentOwner → tags ≥10% holders in the holdings dashboard
        // Drop any single arm and the corresponding flag silently falls to false
        // for the filers using that representation. The "1" arm specifically is
        // the highest-volume one — every filer using boolean-as-integer XML
        // serialization (the majority) flows through it.
        //
        // The risk this catches: a refactor that "simplifies" the pattern to
        // `bool.TryParse(value, out var result) && result` would handle
        // "true"/"True"/"TRUE" but reject "1" — silently misclassifying every
        // is*=1 flag as false. The existing tests in this file don't exercise
        // ParseBool at all (only ParseLong, SanitizeXml, ParseTransactionCode),
        // so this regression would compile cleanly, pass every existing pin,
        // and silently strip the director/officer/10-percent-owner tags from
        // every new InsiderOwner row.
        //
        // Pin "1" specifically — it's the most common SEC form AND the one
        // bool.TryParse rejects (the most likely simplification regression).
        var result = (bool)ParseBoolMethod.Invoke(null, ["1"]);

        result.Should().BeTrue();
    }

    [Fact]
    public void ParseLong_DecimalString_FallsBackToParseDecimalAndTruncates() {
        // SEC Form 4 XML routinely reports fractional share counts in transactionShares
        // and sharesOwnedFollowingTransaction — partial RSU vests, dividend reinvestments,
        // and ESPP fractional allocations all emit values like "1234.5678" rather than
        // a whole-share count. `long.TryParse` rejects these outright, so ParseLong falls
        // back to `(long)ParseDecimal(value)` which parses then truncates toward zero.
        // Without that fallback, every fractional-share transaction would silently store
        // a Shares=0 row, polluting position-history queries and breaking ownership
        // continuity in the holdings view.
        //
        // The risk this test pins: a refactor that drops the decimal fallback (or that
        // swaps `(long)ParseDecimal(value)` for `0`/`-1`) would compile cleanly, pass the
        // existing integration test (whose fixture XML uses whole numbers), and silently
        // zero out every partial-share row in production.
        //
        // 1234.5678 → 1234 specifically distinguishes the decimal-fallback path
        // (returns 1234) from a "truncate to 0 on parse failure" path (returns 0).
        var result = (long)ParseLongMethod.Invoke(null, ["1234.5678"]);

        result.Should().Be(1234L);
    }

    [Fact]
    public void SanitizeXml_PreservesAlreadyEscapedEntities_WhileEscapingBareAmpersand() {
        // SEC Form 3/4 XML payloads routinely contain bare `&` characters in company and
        // owner names ("Smith & Jones") that would crash XDocument.Parse. SanitizeXml's regex
        // — `&(?!(amp|lt|gt|quot|apos|#\d+|#x[\da-fA-F]+);)` — escapes those bare ampersands
        // to `&amp;` WITHOUT double-escaping anything that's already a valid XML entity.
        //
        // The risk this test pins: a "simplification" refactor to `Regex.Replace(xml, "&", "&amp;")`
        // would re-escape `&amp;` to `&amp;amp;`, silently corrupting every insider filing
        // that already used a properly-escaped entity (which the SEC does for legal names
        // like "AT&T Inc."). The corruption is invisible at sanitize time and only surfaces
        // when the parsed XML's `rptOwnerName` contains stray `amp;` characters in the DB.
        //
        // Inputs deliberately cover all six negative-lookahead alternatives plus a bare `&`.
        var input = "<XML><doc><name>AT&amp;T &lt;raw&gt; &quot;x&quot; O&apos;Brien &#65; &#x1F; Smith & Jones</name></doc></XML>";

        var result = (string)SanitizeXmlMethod.Invoke(null, [input]);

        // Bare ampersand became &amp;
        result.Should().Contain("Smith &amp; Jones");
        // Already-escaped entities are unchanged
        result.Should().Contain("AT&amp;T");
        result.Should().NotContain("&amp;amp;");
        result.Should().Contain("&lt;raw&gt;");
        result.Should().Contain("&quot;x&quot;");
        result.Should().Contain("O&apos;Brien");
        result.Should().Contain("&#65;");
        result.Should().Contain("&#x1F;");
    }

    [Fact]
    public void ParseTransactionCode_SaleCodeS_ReturnsSale() {
        // Sibling pin to ParseTransactionCode_PurchaseCodeP_ReturnsPurchase. The
        // existing P→Purchase test catches a P↔S swap (P→Sale fails the assertion),
        // but it does NOT catch an asymmetric regression that breaks only the S arm
        // — e.g., a careless edit that changes `"S" => TransactionCode.Sale` to
        // `"S" => TransactionCode.Other` (or any other arm) would pass the P test
        // cleanly while silently classifying every insider sale as Other. The
        // failure mode is invisible: dashboards that distinguish "insider buying"
        // from "insider selling" would lose the entire Sale signal, while the
        // Purchase signal would still appear in green-build CI.
        //
        // SEC Form 4 `S` is the open-market or private sale code — the single
        // most common bearish-signal code in the corpus (Awards "A" are routine
        // RSU grants and "F" is tax-payment-related, not discretionary). Pin it
        // independently of P so the two highest-volume codes carry symmetric
        // regression coverage. Lowercase input "s" is deliberately chosen to ALSO
        // exercise the `code?.ToUpperInvariant()` normalization branch — without
        // ToUpperInvariant, lowercase wire payloads would fall through to the
        // default `_ => Other` arm. The pair (P→Purchase, s→Sale) covers BOTH
        // the high-volume mapping AND the case-normalization step in two pins.
        var result = (TransactionCode)ParseTransactionCodeMethod.Invoke(null, ["s"]);

        result.Should().Be(TransactionCode.Sale);
    }

    [Fact]
    public void ParseTransactionCode_AwardCodeA_ReturnsAward() {
        // Third sibling in the ParseTransactionCode family (after P→Purchase and
        // s→Sale). Award (`A`) is the single highest-volume code in the entire SEC
        // Form 4 corpus — every quarterly RSU vest, every executive performance
        // grant, every annual director-compensation award flows through it. By
        // count, Awards typically outnumber Purchases AND Sales combined in the
        // production InsiderTransaction table.
        //
        // The risk this catches is asymmetric and unreachable from the P/S
        // siblings: a regression that breaks only the A arm — e.g. a copy-paste
        // edit that changes `"A" => TransactionCode.Award` to
        // `"A" => TransactionCode.Other` (or shifts the line out of the switch
        // entirely during a "tidy-up alphabetical order" refactor) — would
        // compile cleanly, pass the existing P and s pins, and silently break
        // the entire awards pipeline. The downstream consequences are concrete:
        //
        //   • The "insider activity overview" dashboard groups by TransactionCode;
        //     Awards would shift into Other and disappear from the "RSU vests
        //     this quarter" view that compensation-committee analysts query.
        //   • Cluster-detection analytics (executive-confidence scoring) rely
        //     on distinguishing routine Awards from discretionary Purchases
        //     to weigh purchase signal. Mis-classified Awards would inflate
        //     the apparent Purchase signal in noisy ways.
        //   • The CSV export bucket "Award" would silently empty out for new
        //     filings, breaking downstream consumers of the export.
        //
        // Pin A → Award specifically so the highest-volume code carries its own
        // dedicated regression check, independent of the P and S siblings.
        var result = (TransactionCode)ParseTransactionCodeMethod.Invoke(null, ["A"]);

        result.Should().Be(TransactionCode.Award);
    }

    [Fact]
    public void ParseTransactionCode_PurchaseCodeP_ReturnsPurchase() {
        // SEC Form 4 transaction codes are single letters that map to specific
        // insider-trade categories per §16 of the Exchange Act. ParseTransactionCode
        // is the switch that translates each wire letter to the domain
        // `TransactionCode` enum the rest of the system reads. The two highest-volume
        // codes — `P` (open-market or private purchase) and `S` (open-market or private
        // sale) — drive every "insider buying" / "insider selling" dashboard on the
        // public site, the CSV exports, the MCP `GetInsiderTransactions` tool, and the
        // alerting pipeline that flags clusters of director purchases.
        //
        // The risk this catches is asymmetric and INVISIBLE to the existing tests: a
        // regression that swapped two switch arms (e.g. `"P" => Sale, "S" => Purchase`
        // due to a careless reorder during a "tidy-up the cases alphabetically" refactor,
        // or a copy-paste edit that touched the wrong line) would compile cleanly, pass
        // the existing `ParseLong` + `SanitizeXml` pins, and pass the upstream integration
        // tests that don't read TransactionCode back through the switch. Every insider
        // purchase from that point forward would silently classify as a Sale and vice
        // versa, inverting the SIGNAL that drives the entire insider-trading product:
        // dashboards would show "selling pressure" when insiders are actually
        // accumulating, alerting would page on the wrong direction, and downstream
        // analytics (cluster-buy detection, executive-confidence scores) would invert
        // their conclusions.
        //
        // Pin the P → Purchase mapping specifically because:
        //   1. It's the most common non-Award code in the corpus (Awards "A" are routine
        //      RSU grants, but P represents discretionary executive decisions — the
        //      genuinely informative signal).
        //   2. The P↔S swap is the single most likely accidental inversion (adjacent in
        //      the source switch, related conceptually, easy to fat-finger).
        //   3. Sibling to the existing `ParseLong` + `SanitizeXml` pins on private
        //      static helpers in this same file — extends the pattern naturally.
        var result = (TransactionCode)ParseTransactionCodeMethod.Invoke(null, ["P"]);

        result.Should().Be(TransactionCode.Purchase);
    }

    [Fact]
    public void ParseTransactionCode_UnknownCodeLetter_ReturnsOther() {
        // Fourth pin in the ParseTransactionCode family. Existing pins cover P, S,
        // and A (each mapped to a concrete TransactionCode value). This pin covers
        // the `_ => TransactionCode.Other` default arm — the catch-all that ensures
        // the switch expression always returns a value rather than throwing
        // SwitchExpressionException.
        //
        // The risk this catches: a refactor that "tidies up" the switch by
        // removing the default arm (or replacing it with a throw clause) would
        // compile cleanly, pass every mapped-code sibling pin, and silently
        // crash the InsiderTradingFilingProcessor on the FIRST filing carrying
        // an unmapped code letter. SEC's §16 code set has expanded over the
        // years — new codes have been added (e.g. "U" for "stocked-up gift"
        // didn't exist in the original 1934 Act) — and any future SEC
        // amendment that introduces a new letter would surface as a runtime
        // crash rather than a silently-bucketed Other-tagged row. The throw
        // would bubble up through the foreach in Process, abort the entire
        // batch, and rollback the import scope — silently losing every
        // filing in the same batch.
        //
        // Pin "Q" — a letter that's currently unused in SEC §16 (the
        // existing assigned letters span A-Z minus a few). Any future
        // assignment to "Q" would simply add a new switch arm; the default
        // catch-all is what keeps the unmapped state from becoming an
        // exception until that mapping is added. Asserting `Other`
        // specifically (not just "doesn't throw") ensures a regression that
        // typo'd the default value (e.g. `_ => TransactionCode.Purchase` —
        // accidentally pasted from the P arm during a refactor) would also
        // surface here.
        var result = (TransactionCode)ParseTransactionCodeMethod.Invoke(null, ["Q"]);

        result.Should().Be(TransactionCode.Other);
    }

    [Fact]
    public void ParseTransactionCode_ConversionCodeM_ReturnsConversion() {
        // Fifth pin in the ParseTransactionCode family. Existing pins cover
        // the highest-volume codes (P/Purchase, S/Sale, A/Award) and the
        // default arm (Other). This pin covers `M => TransactionCode.Conversion` —
        // the conversion of a derivative security (option, warrant, RSU) into
        // common stock. M is the SECOND-most common code in §16 filings after
        // A (Award), because every executive option exercise produces a paired
        // M (Conversion of derivative) + S (Sale of underlying) row. Without
        // the M arm, conversion rows would silently fall through to the
        // default `Other` bucket.
        //
        // The risk this catches is distinct from the P↔S swap and the Award
        // pins: the conversion bucket drives the "executive options exercised"
        // dashboard and feeds the dilution-tracking analytics. A refactor that
        // drops the M arm — easy to do during a "consolidate similar codes"
        // cleanup that merges M into the catch-all on the assumption that
        // conversions aren't business-relevant — would compile, pass every
        // other ParseTransactionCode pin (P, S, A, default), and silently
        // demote every option-exercise event in the corpus from a labeled
        // "Conversion" to a generic "Other". Downstream consumers would lose
        // the ability to distinguish "executive cashed in options" from
        // "filed a random uncategorized form", wrecking the option-exercise
        // chart on the insider trading page.
        //
        // The mapping M => Conversion is also semantically subtle: M does
        // NOT mean "modification" or "miscellaneous" — it specifically means
        // conversion per §16 schedule. A refactor that "improves clarity"
        // by renaming the enum value from Conversion to something else
        // would also fail this pin if the literal value changes.
        //
        // Pin uppercase "M" — ParseTransactionCode normalizes via
        // ToUpperInvariant before matching, but pinning the canonical
        // uppercase form documents the expected XML-element wire shape from
        // SEC EDGAR (which always emits uppercase code letters).
        var result = (TransactionCode)ParseTransactionCodeMethod.Invoke(null, ["M"]);

        result.Should().Be(TransactionCode.Conversion);
    }

    [Fact]
    public void ParseTransactionCode_ExerciseCodeX_ReturnsExercise() {
        // Sixth pin in the ParseTransactionCode family. Existing pins cover
        // P/Purchase, S/Sale, A/Award, _/Other, and M/Conversion. This pin
        // covers `X => TransactionCode.Exercise` — the exercise of a
        // derivative security (option, warrant). X is structurally related
        // to but distinct from M: X is the option/warrant exercise event
        // itself (the moment the derivative is converted into the right to
        // receive common stock), where M is the corresponding receipt of
        // the underlying common shares. In practice the two often arrive
        // in adjacent rows on the same Form 4 — X writes off the
        // derivative position, M records the common-stock acquisition —
        // and the two MUST classify into distinct buckets so the
        // option-exercise analytics can pair them up.
        //
        // The risk this catches is distinct from the M pin: a refactor
        // that "consolidates" X into M (under the false intuition that
        // they describe the same business event) would compile, pass the
        // existing pins, and silently merge two operational streams. The
        // option-exercise chart's pairing logic — which counts X events to
        // detect cluster-exercise behavior across executives — would
        // double-count or skip entirely depending on which arm got
        // merged. The pair-up between X and M is the foundation of the
        // dilution-tracking series; collapsing them inverts which
        // executive bought (M) vs. which option lot they tapped (X).
        //
        // The mapping X => Exercise is also semantically subtle in the
        // OTHER direction: a refactor that swapped the X arm with
        // F/TaxPayment (adjacent in source order, both single-letter
        // codes that look similar at a glance) would compile, pass the
        // M pin, and silently misclassify every option exercise as a
        // tax payment — a category mix-up that would corrupt both
        // analytics streams at once.
        //
        // Pin uppercase "X" specifically. ParseTransactionCode normalizes
        // via ToUpperInvariant before matching but the canonical wire
        // form from SEC EDGAR is always uppercase.
        var result = (TransactionCode)ParseTransactionCodeMethod.Invoke(null, ["X"]);

        result.Should().Be(TransactionCode.Exercise);
    }
}
