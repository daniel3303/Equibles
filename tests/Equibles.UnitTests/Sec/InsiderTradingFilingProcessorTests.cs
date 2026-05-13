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
    public void ParseBool_DigitZero_ReturnsFalseViaImplicitDefaultArm() {
        // Sibling pin to ParseBool_DigitOne_ReturnsTrue. The existing pin establishes
        // that "1" maps to true via the four-arm `is "1" or "true" or "True" or "TRUE"`
        // pattern match. This pin establishes that "0" — the explicit-negative encoding
        // SEC Form 4 XML uses to indicate "not a director/officer/10%-owner" — maps to
        // false via the implicit default arm (the pattern match falls through to false
        // for anything not in the four-literal allowlist).
        //
        // The risk this catches is asymmetric and unreachable from the digit-1 sibling:
        //   • A refactor that "simplifies" the predicate to `value != "0"` (intuitive
        //     to anyone reading the SEC schema, where the entire ecosystem treats "1"
        //     and "0" as the only two values) would compile cleanly, pass the digit-1
        //     pin ("1" != "0" → true ✓), AND silently flip every other string-shaped
        //     value to true — including "false" itself, the lowercase "true" siblings,
        //     and any empty/unparseable XML text node. Every <isOfficer>false</isOfficer>
        //     in non-conforming filer output would suddenly classify the reporter as an
        //     officer, polluting the officer-purchase-cluster alerting pipeline with
        //     false positives for non-officer filers.
        //   • A swap-with-default-true regression (someone defaults to true on parse
        //     failure under the assumption "if in doubt, surface the role") would
        //     produce the same symptom: every <isDirector>0</isDirector> would
        //     register the filer as a director, polluting the directors-buying
        //     dashboard with retail and 10%-owner traffic.
        //
        // The "0" digit is the COMPLEMENT of the existing pin's "1" digit — both are
        // the canonical SEC XML encoding for boolean section-16 role flags, and they
        // appear with roughly equal frequency in production XML (every filing has
        // exactly one is*=1 set and three is*=0 set, since reporters are usually only
        // ONE of director/officer/10%-owner/other). Together, the two pins prove that
        // the predicate handles both halves of the canonical encoding correctly —
        // a much stronger guarantee than the digit-1 pin alone.
        //
        // Assert that "0" produces false. A regression that flipped this would surface
        // immediately here.
        var result = (bool)ParseBoolMethod.Invoke(null, ["0"]);

        result.Should().BeFalse();
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

    [Fact]
    public void ParseTransactionCode_TaxPaymentCodeF_ReturnsTaxPayment() {
        // Seventh pin in the ParseTransactionCode family. Existing pins cover
        // P/Purchase, S/Sale, A/Award, _/Other, M/Conversion, and X/Exercise.
        // This pin covers `F => TransactionCode.TaxPayment` — payment of
        // tax-withholding obligations on a vested RSU/PSU grant by surrendering
        // shares back to the issuer.
        //
        // F is critically distinct from S (Sale) on the analytics surface
        // even though both describe a share-OUT event:
        //   • S = open-market or private sale — the executive received cash
        //     and chose to liquidate. Signal value: HIGH (executive
        //     confidence / cash needs).
        //   • F = tax-withholding sell-back — the executive did NOT choose
        //     to sell; the company sold shares to fund payroll-tax
        //     withholding on a vesting event. Signal value: NONE (purely
        //     mechanical, happens to every vest no matter the executive's
        //     view on the stock).
        // Mixing the two corrupts the "insider selling pressure" indicator
        // that powers the public-site dashboard and the alerting pipeline.
        // Every vesting event would inflate the apparent selling signal,
        // pushing the "insiders are selling" flag on for routine
        // compensation events.
        //
        // The risk this catches is the FOR-tax-withholding misclassification
        // — a refactor that "consolidates F into S" (under the false intuition
        // that both describe outflows from the executive) would compile,
        // pass the existing P/S/A/M/X/default pins, and silently flip every
        // tax-withholding row into the high-signal Sale bucket. The
        // signal-to-noise ratio of the insider-selling dashboard would
        // tank: ~30-50% of all S rows in the corpus would now be
        // mechanical tax events misclassified as discretionary sales.
        //
        // The complementary risk: collapsing F into Other (the default
        // arm) would silently bucket tax-payment events as
        // uncategorized, losing the ability to filter THEM OUT of selling
        // analytics — same end state but reached via a different
        // simplification mistake.
        //
        // Pin uppercase "F". ParseTransactionCode normalizes via
        // ToUpperInvariant before matching but the canonical wire form
        // from SEC EDGAR is always uppercase.
        var result = (TransactionCode)ParseTransactionCodeMethod.Invoke(null, ["F"]);

        result.Should().Be(TransactionCode.TaxPayment);
    }

    [Fact]
    public void ParseTransactionCode_GiftCodeG_ReturnsGift() {
        // Eighth pin in the ParseTransactionCode family. Existing pins cover
        // P/Purchase, S/Sale, A/Award, _/Other, M/Conversion, X/Exercise,
        // and F/TaxPayment. This pin covers `G => TransactionCode.Gift` —
        // bona-fide gifts of securities by the insider (charitable donations,
        // gifts to family/trusts, etc.).
        //
        // G is critically distinct from S (Sale) on the analytics surface
        // for the same family of reasons F is: both describe a share-OUT
        // event but the SIGNAL VALUE differs sharply.
        //   • S = open-market sale — the executive received cash and chose
        //     to liquidate. Drives the "insider selling" indicator.
        //   • G = gift — no cash to the executive; the insider transferred
        //     ownership but received NOTHING in return. Charitable
        //     donations and family-trust transfers are the dominant forms.
        //     Signal value: zero for the selling-pressure metric.
        // Collapsing G into S would inflate apparent selling pressure with
        // mechanical donations (and charitable giving is highly seasonal —
        // a December spike in apparent insider selling would falsely
        // appear every year).
        //
        // The risk this catches: a refactor that "consolidates" G into S
        // (similar reasoning to the F→S risk) would compile, pass every
        // mapped-code sibling pin (P, S, A, M, X, F) plus the default
        // (Other), and silently flip every gift row into the high-signal
        // Sale bucket. Insider-selling dashboards spike every December
        // from charitable giving. The complementary risk: G→Other would
        // silently drop the gift bucket from filterable analytics. The
        // current G arm specifically tags these as Gift so the public
        // insider page CAN distinguish "executive gave shares to a
        // charity" from "executive sold for cash" in the per-insider
        // breakdown.
        //
        // A subtler regression: G is also visually adjacent to F in the
        // switch arm ordering and conceptually adjacent in "share-OUT-
        // but-not-a-real-sale" land. A copy-paste edit that touched the
        // wrong arm could produce `"G" => TransactionCode.TaxPayment` —
        // every donation would silently classify as a tax payment,
        // muddying both buckets.
        //
        // Pin uppercase "G". ParseTransactionCode normalizes via
        // ToUpperInvariant before matching but the canonical wire form
        // from SEC EDGAR is always uppercase.
        var result = (TransactionCode)ParseTransactionCodeMethod.Invoke(null, ["G"]);

        result.Should().Be(TransactionCode.Gift);
    }

    [Fact]
    public void ParseTransactionCode_DiscretionaryCodeW_ReturnsDiscretionary() {
        // Ninth pin in the ParseTransactionCode family. Existing pins cover
        // P/Purchase, S/Sale, A/Award, _/Other, M/Conversion, X/Exercise,
        // F/TaxPayment, and G/Gift. This pin covers
        // `W => TransactionCode.Discretionary` — acquisitions or
        // dispositions made via a 10b5-1 trading plan or other pre-
        // authorized discretionary mechanism that doesn't fit the
        // open-market / award / gift / tax-withholding categories.
        //
        // W is the LAST single-letter §16 code that's semantically
        // distinct AND used in production filings (E for Expiration and
        // I for Inheritance round out the table but are statistically
        // rarer). It's the most semantically subtle of the codes: a
        // "Discretionary" transaction is one the insider authorized in
        // advance through a plan but executed mechanically — the
        // signal-value tier sits between the genuinely informative
        // open-market P/S (high signal) and the mechanical F/TaxPayment
        // (no signal). Misclassifying W in either direction silently
        // miscalibrates the insider-trading signal.
        //
        // The risk uniquely caught by this pin: a refactor that swapped
        // the W arm with an adjacent letter's mapping (W appears LAST
        // in the switch source — the natural "tail" position is the
        // most likely to be lost in a "consolidate the tail arms"
        // refactor that merges W/I/E into the default catch-all on the
        // assumption that "rare codes can fall through to Other").
        // Such a refactor would compile, pass every other ParseTransactionCode
        // pin (P/S/A/M/X/F/G/Other), and silently demote 10b5-1 trades
        // — the regulator-blessed mechanism executives use to time sales
        // around earnings windows — into the uncategorized Other bucket.
        // Downstream analytics that filter on Discretionary specifically
        // (e.g. "did the CEO sell via plan or via discretion this quarter?")
        // would silently return zero results.
        //
        // The complementary risk: a swap with G/Gift (also in the
        // "non-open-market" semantic cluster) would silently merge two
        // distinct disclosure categories, corrupting both the gift
        // donation aggregate (used in charitable-giving analytics) AND
        // the 10b5-1 plan-execution aggregate (used in insider-trading
        // pattern detection). Pin uppercase "W" with assertion on the
        // exact enum value Discretionary so any of these regressions
        // surface here.
        var result = (TransactionCode)ParseTransactionCodeMethod.Invoke(null, ["W"]);

        result.Should().Be(TransactionCode.Discretionary);
    }

    [Fact]
    public void ParseTransactionCode_ExpirationCodeE_ReturnsExpiration() {
        // Tenth pin in the ParseTransactionCode family. Existing pins cover
        // P/Purchase, S/Sale, A/Award, _/Other, M/Conversion, X/Exercise,
        // F/TaxPayment, G/Gift, and W/Discretionary. This pin covers
        // `E => TransactionCode.Expiration` — the SEC Form 4 code for
        // "Expiration of short derivative position", filed when a previously
        // disclosed short option, warrant, or other derivative held by an
        // insider expires worthless (without exercise).
        //
        // E sits in the "lifecycle" semantic cluster alongside X (Exercise)
        // and M (Conversion) — all three terminate the life of a derivative
        // position. The codebase maps them to three distinct TransactionCode
        // values precisely so downstream analytics can distinguish them:
        //   • Exercise (X) — the insider USED the derivative, converting it
        //     to underlying shares at the strike. Real economic decision.
        //   • Conversion (M) — Rule 16b-3-exempt conversion, mechanical.
        //   • Expiration (E) — the derivative reached its expiry date
        //     without exercise. NO economic decision — passive event.
        //
        // The risk this pin uniquely catches is asymmetric and unreachable
        // from the X/Exercise and M/Conversion siblings: a refactor that
        // collapsed E into X (under the mistaken assumption that "expired
        // and exercised both terminate the position, same thing") would
        // compile cleanly, pass every existing ParseTransactionCode pin (X
        // still maps to Exercise, M still maps to Conversion, all the others
        // are untouched), and silently re-tag every expired option as a
        // discretionary Exercise. Downstream insider-trading analytics built
        // on Exercise counts would inflate by the population of expired
        // derivatives — which is large during bear markets, when underwater
        // options expire en masse. The signal-quality degradation is
        // exactly the kind operators can't visually detect (a 20% lift in
        // "Exercise" counts looks like increased insider activity rather
        // than the bug it actually is).
        //
        // The complementary risk: a swap with the default arm (E falls
        // through to Other) would lose every expiration event from the
        // typed enum entirely, breaking any filter that targets Expiration
        // specifically. Both regressions surface as a wrong enum value on
        // this pin's assertion.
        //
        // The two remaining unpinned arms after this pin are I/Inheritance
        // and the null-code path (`code?.ToUpperInvariant()` propagates null
        // into the default arm). Pin uppercase "E" with assertion on the
        // exact enum value Expiration so any of the lifecycle-cluster
        // regressions surface here.
        var result = (TransactionCode)ParseTransactionCodeMethod.Invoke(null, ["E"]);

        result.Should().Be(TransactionCode.Expiration);
    }

    [Fact]
    public void ParseTransactionCode_InheritanceCodeI_ReturnsInheritance() {
        // Eleventh pin in the ParseTransactionCode family, completing every
        // letter-arm of the switch. Existing pins cover P/Purchase, S/Sale,
        // A/Award, _/Other, M/Conversion, X/Exercise, F/TaxPayment, G/Gift,
        // W/Discretionary, and E/Expiration. This pin covers
        // `I => TransactionCode.Inheritance` — the codebase's mapping for
        // securities acquired via the death of a prior holder (transfer by
        // will, intestacy, or beneficiary designation).
        //
        // I sits in the "non-economic acquisition" semantic cluster alongside
        // G/Gift — both represent securities entering the insider's holdings
        // WITHOUT a market transaction or insider decision. Downstream
        // analytics distinguish them:
        //   • Gift (G) — donor was alive at the time of transfer. Donor
        //     identity is reportable; the gift is a deliberate act with
        //     tax-planning implications.
        //   • Inheritance (I) — prior holder is deceased. The acquisition is
        //     a passive legal event; no donor relationship exists.
        // Misclassifying I as G (or vice versa) would corrupt both the
        // charitable-giving analytics (Gift aggregate) AND the estate-
        // transfer analytics (Inheritance aggregate) — two distinct
        // disclosure categories the dashboard surfaces separately.
        //
        // The risk this pin uniquely catches is asymmetric and unreachable
        // from the G/Gift sibling: a refactor that collapsed I into G (under
        // the mistaken assumption that "both are non-purchase acquisitions,
        // same bucket") would compile cleanly, pass every existing
        // ParseTransactionCode pin (G still maps to Gift, all 10 others are
        // untouched), and silently re-tag every inheritance event as a
        // discretionary Gift. The volume signal is small but meaningful —
        // founder/executive deaths produce concentrated Inheritance reports
        // that estate-planning analytics filter on specifically; merging
        // them into Gift would erase the signal entirely.
        //
        // The complementary risk: a swap with the default arm (I falls
        // through to Other) would lose every inheritance event from the
        // typed enum entirely, breaking any filter that targets Inheritance
        // specifically. Both regressions surface as a wrong enum value on
        // this pin's assertion.
        //
        // With this pin, all 10 letter arms (P, S, A, M, X, F, E, G, I, W)
        // plus the default arm are individually pinned. The only path NOT
        // exercised by a sibling pin is the `code?.ToUpperInvariant()` null
        // propagation — null input bypasses the switch entirely and falls
        // through to `_ => Other` by way of `null switch { ... _ => Other }`.
        // That path's behavior is established transitively by the existing
        // default-arm pin (which uses "Q", an unknown letter) — the same
        // default-arm code emits Other regardless of which way it was
        // reached. Pin uppercase "I" with assertion on the exact enum value
        // Inheritance so the swap-with-Gift and swap-with-default
        // regressions surface here.
        var result = (TransactionCode)ParseTransactionCodeMethod.Invoke(null, ["I"]);

        result.Should().Be(TransactionCode.Inheritance);
    }

    [Fact]
    public void ParseBool_LowercaseTrueLiteral_ReturnsTrueViaSecondArm() {
        // Third pin in the ParseBool family. Existing pins cover the
        // "1" arm (DigitOne) and the default-Negative arm
        // (DigitZero → false via implicit fall-through). This pin
        // covers the SECOND of the three string arms in
        //   `value is "1" or "true" or "True" or "TRUE"`
        //
        // Why the three case-variants matter: SEC Form 4 XML's
        // schema doesn't constrain the wire form of boolean
        // <isDirector>/<isOfficer>/<isTenPercentOwner> elements.
        // Different filers (and different filer agents — Workiva,
        // Donnelley Financial, Toppan Merrill, manual EDGAR
        // submissions) emit any of "1", "true", "True", "TRUE" in
        // production. The four-arm matcher is the production code
        // saying "all four forms are equally valid 'yes'." A
        // refactor that consolidates the three string arms via
        // `string.Equals(value, "true", OrdinalIgnoreCase)` would
        // be functionally equivalent on the current input domain
        // BUT lose the load-bearing distinction the case-variants
        // signal:
        //   • The explicit case enumeration documents which wire
        //     forms have been observed in production. Dropping it
        //     to a case-insensitive comparison loses that audit
        //     trail.
        //   • A switch to `value.ToLowerInvariant() == "true"`
        //     introduces a string allocation per parse on every
        //     Form 4 (every insider transaction in the database is
        //     a parse-this-XML call). The pattern-match version
        //     is allocation-free.
        //
        // The risk this pin uniquely catches: a refactor that
        // drops the `"true"` arm specifically (under the false
        // intuition that "we always see 1 or TRUE, never
        // lowercase") would compile, pass DigitOne and DigitZero,
        // and silently misclassify every Form 4 from filers that
        // emit lowercase "true" as isDirector=false /
        // isOfficer=false. The downstream consequence: the
        // "directors buying" dashboard would silently drop those
        // filers' transactions; the cluster-purchase alert
        // pipeline would never trigger on lowercase-emitting
        // filers.
        //
        // Pin lowercase "true" — the form most likely to be lost
        // in a "we always normalize to uppercase" refactor. The
        // True (mixed case) and TRUE (all caps) arms remain
        // unpinned but follow the same risk profile; this is the
        // canonical case-variant arm to test because lowercase is
        // the most natural-looking pre-normalization form.
        var result = (bool)ParseBoolMethod.Invoke(null, ["true"]);

        result.Should().BeTrue();
    }

    [Fact]
    public void ParseBool_TitleCaseTrueLiteral_ReturnsTrueViaThirdArm() {
        // Fourth pin in the ParseBool family. Existing pins cover "1"
        // (DigitOne), "0" (DigitZero → default false), and lowercase
        // "true" (LowercaseTrue → second arm). This pin covers the
        // THIRD of the three string arms in:
        //   `value is "1" or "true" or "True" or "TRUE"`
        //
        // Why "True" (TitleCase) uniquely matters:
        //   • It's the form .NET's `bool.ToString()` produces. Any
        //     filer/agent that round-trips a boolean through a .NET
        //     intermediate (System.Boolean serialized to string and
        //     written back to XML without explicit format control)
        //     emits exactly "True". This is the most common wire form
        //     for filers using .NET-based EDGAR submission toolkits.
        //   • Java's Boolean.toString() emits lowercase "true"
        //     (covered by the existing sibling pin).
        //   • Pure-XML serialization libraries that follow XSD
        //     primitives ({"true", "false", "1", "0"} per W3C
        //     XML Schema Part 2) emit lowercase per spec.
        //   So "True" specifically tags the .NET-tooling filer
        //   population — a distinct population from lowercase-true
        //   (Java/XML-spec-conformant) and "TRUE" (legacy/manual).
        //
        // The risk this pin uniquely catches is asymmetric and
        // unreachable from the lowercase-true sibling:
        //   • A refactor that drops the "True" arm specifically
        //     (under the false intuition that "all lowercase variants
        //     are covered by the second arm + the case-insensitive
        //     pattern would handle anything else") would compile,
        //     pass the DigitOne pin (digit branch untouched), pass
        //     the DigitZero pin (default arm untouched), pass the
        //     LowercaseTrue pin (lowercase arm untouched), and
        //     silently misclassify every Form 4 from .NET-tooling
        //     filers as isDirector=false / isOfficer=false. This is
        //     the WORKIVA/CERTENT-style filer population — a major
        //     fraction of corporate Form 4 submissions, since most
        //     public companies use professional filer agents on .NET.
        //   • A swap with one of the other true-arms (e.g. someone
        //     "consolidates" by dropping "True" and adding
        //     `.ToUpperInvariant() == "TRUE"` before the switch)
        //     would still drop the pattern-match-direct path AND
        //     introduce an allocation per parse — same load-bearing
        //     micro-perf regression flagged in the LowercaseTrue
        //     pin's commentary.
        //
        // Pin TitleCase "True" — the form most uniquely tied to a
        // specific filer-tooling population. The "TRUE" (all-caps)
        // arm remains unpinned but follows the same shape; closing
        // it completes the four-arm family (1 + true + True + TRUE).
        var result = (bool)ParseBoolMethod.Invoke(null, ["True"]);

        result.Should().BeTrue();
    }

    [Fact]
    public void ParseBool_AllCapsTrueLiteral_ReturnsTrueViaFourthArm() {
        // Fifth and final pin in the ParseBool family. With this pin, all
        // five arms of the four-string-literal-plus-default pattern are
        // individually pinned: "1" (DigitOne), "0" (DigitZero → default
        // false), "true" (Lowercase → second arm), "True" (TitleCase →
        // third arm), and now "TRUE" (AllCaps → fourth arm) closing
        //   `value is "1" or "true" or "True" or "TRUE"`
        //
        // Why "TRUE" (all-caps) uniquely matters and is unreachable
        // from the three existing string-arm siblings:
        //   • All-caps "TRUE" is the form emitted by:
        //     - Legacy COBOL/RPG-style serializers (some EDGAR-adjacent
        //       tooling still in use for manual filings)
        //     - SQL-export pipelines that uppercase every column value
        //     - Hand-typed filings where the filer typed in caps lock
        //     - Some XSL transformations that explicitly normalize to
        //       UPPER per legacy schema conventions
        //     This is a distinct filer population from
        //     lowercase-true (Java/XML-spec-conformant), TitleCase True
        //     (.NET tooling), and digit-1 (numeric-encoded).
        //   • The pattern match `value is ... or "TRUE"` short-circuits
        //     on the first matching alternative. A regression that
        //     drops the "TRUE" arm specifically (under the false
        //     intuition that "no production filer emits all-caps;
        //     anyone using caps lock would also fail
        //     the unit-test fixtures") would compile, pass DigitOne,
        //     DigitZero, LowercaseTrue, AND TitleCaseTrue pins — yet
        //     silently misclassify every legacy/SQL-pipeline-derived
        //     Form 4 as isDirector=false / isOfficer=false. The
        //     downstream consequences mirror the other case-variant
        //     pins: the directors-buying dashboard would drop the
        //     legacy-tooling filer population, the cluster-purchase
        //     alert pipeline would silently miss their transactions.
        //
        // The complementary risk: a refactor that "consolidates" the
        // case variants via `string.Equals(value, "true",
        // OrdinalIgnoreCase)` would be functionally equivalent on the
        // current input domain BUT lose the load-bearing distinction
        // the case-variants signal (the explicit enumeration is
        // production-observed input documentation). Pinning every
        // case-variant arm individually keeps the audit trail intact —
        // a future refactor that drops the explicit cases would have
        // to update FIVE tests, not one, making the regression
        // deliberate.
        //
        // The four-arm family is now complete:
        //   • "1"    → DigitOne pin (pinned)
        //   • "true" → Lowercase pin (pinned)
        //   • "True" → TitleCase pin (pinned)
        //   • "TRUE" → AllCaps pin (this pin)
        //   • default → DigitZero pin (false via implicit fall-through)
        //
        // Pin all-caps "TRUE" with assertion on the exact bool value
        // true. Any regression touching the fourth arm — drop, swap,
        // or accidental case-change — surfaces here.
        var result = (bool)ParseBoolMethod.Invoke(null, ["TRUE"]);

        result.Should().BeTrue();
    }

    [Fact]
    public void ParseBool_NullInput_ReturnsFalseViaPatternMatchFallThrough() {
        // Sixth pin in the ParseBool family. The previous five pins cover the
        // four string-literal arms of `value is "1" or "true" or "True" or "TRUE"`
        // (DigitOne, LowercaseTrue, TitleCaseTrue, AllCapsTrue) plus the
        // implicit-default `"0"` non-match case. This pin covers the structurally
        // distinct **null** input — the C# `is` pattern handles `null` safely
        // by returning false for every constant-string arm, so a null value
        // falls through to the default-false branch without throwing.
        //
        // Null is the dominant unhappy-path input in production. Every call
        // site passes the result of an XLinq chain that can short-circuit:
        //   IsDirector = ParseBool(ownerRelationship?.Element("isDirector")?.Value)
        //   IsOfficer  = ParseBool(ownerRelationship?.Element("isOfficer")?.Value)
        //   IsTenPercentOwner = ParseBool(ownerRelationship?.Element("isTenPercentOwner")?.Value)
        // When the `<ownerRelationship>` element is absent (legitimate for
        // Form 4 amendments and edge-case filings), or when one of the three
        // child elements is missing (some filers omit the flag they're
        // setting to false rather than emitting `<isOfficer>0</isOfficer>`),
        // `?.Value` evaluates to null and that null lands in ParseBool.
        //
        // The risk this catches is asymmetric and unreachable from every
        // existing sibling pin: a refactor that swaps the pattern-match
        // expression for an `Equals`-style call would compile cleanly,
        // pass ALL five existing pins (which feed non-null inputs), and
        // throw `NullReferenceException` on the very first XML row with
        // a missing `<isOfficer>` element. Realistic regression patterns:
        //
        //   • `return value.Equals("1") || value.Equals("true") || ...`
        //     — the "let me harmonize with the rest of the file" refactor
        //     pattern: ParseTransactionCode uses `switch (value.ToUpper())`
        //     several lines above, and a contributor reading the file
        //     might "consistency-fix" ParseBool to match. NRE on the very
        //     first call site that received null from `?.Value`.
        //
        //   • `return value.Equals("1", StringComparison.OrdinalIgnoreCase)`
        //     — the "merge the four case-variant arms into one" cleanup.
        //     Compiles cleanly, looks cleaner, NREs on null.
        //
        //   • `return new[] { "1", "true", "True", "TRUE" }.Contains(value)`
        //     — Contains DOES handle null safely (returns false for a null
        //     element since no array entry is null), so THIS specific
        //     refactor would not regress this pin. Worth noting because it
        //     means this pin doesn't catch every conceivable refactor —
        //     but the dangerous ones (the Equals-style ones that NRE
        //     in production) are the ones the C# `is` pattern's null-
        //     safety protects against, and that's exactly what this
        //     pin defends.
        //
        // The downstream impact of an NRE: ParseBool is called inside the
        // XML parsing loop in BuildInsiderOwner (line ~107) for every
        // owner relationship. An unhandled NRE would propagate up through
        // BuildInsiderOwner → ProcessFiling → out of the per-filing try-catch
        // (which catches Exception but logs at Error level and increments
        // result.Errors), corrupting the per-filing error count and
        // potentially aborting the entire scrape cycle if the error
        // handling chain decides to bail. Same impact symptom as the
        // case-variant regressions — silent data loss — but a different
        // failure MODE (crash-and-rollback vs. silent misclassification).
        // Both modes are equally invisible past the log noise.
        //
        // Pin null specifically and assert false (not "doesn't throw" —
        // assert the concrete return value). The dual proof that
        //   (a) the call succeeded (no exception)
        //   (b) the return was false (default fall-through, not an
        //       accidental true)
        // together distinguish a working pattern-match guard from BOTH
        // the NRE regressions AND a "default to true on null" refactor.
        var result = (bool)ParseBoolMethod.Invoke(null, [null]);

        result.Should().BeFalse();
    }
}
