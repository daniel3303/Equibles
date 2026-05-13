using System.Reflection;
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
}
