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
}
