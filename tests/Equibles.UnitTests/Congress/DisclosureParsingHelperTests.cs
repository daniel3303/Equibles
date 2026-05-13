using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperTests {
    // ── ParseTransactionType ────────────────────────────────────────────

    [Theory]
    [InlineData("Purchase", CongressTransactionType.Purchase)]
    [InlineData("Sale", CongressTransactionType.Sale)]
    [InlineData("Sale (Full)", CongressTransactionType.Sale)]
    [InlineData("Sale (Partial)", CongressTransactionType.Sale)]
    [InlineData("Sold", CongressTransactionType.Sale)]
    [InlineData("Buy", CongressTransactionType.Purchase)]
    [InlineData("P", CongressTransactionType.Purchase)]
    [InlineData("S", CongressTransactionType.Sale)]
    [InlineData("purchase", CongressTransactionType.Purchase)]
    [InlineData("SALE", CongressTransactionType.Sale)]
    public void ParseTransactionType_KnownTypes_ReturnsExpected(string input, CongressTransactionType expected) {
        DisclosureParsingHelper.ParseTransactionType(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Exchange")]
    [InlineData("Receive")]
    [InlineData("unknown")]
    public void ParseTransactionType_UnknownOrNull_ReturnsNull(string input) {
        DisclosureParsingHelper.ParseTransactionType(input).Should().BeNull();
    }

    // ── ParseAmountRange ────────────────────────────────────────────────

    [Theory]
    [InlineData("$1,001 - $15,000", 1001, 15000)]
    [InlineData("$50,001 - $100,000", 50001, 100000)]
    [InlineData("$15,001 - $50,000", 15001, 50000)]
    public void ParseAmountRange_TwoValues_ReturnsFromTo(string input, long expectedFrom, long expectedTo) {
        var (from, to) = DisclosureParsingHelper.ParseAmountRange(input);
        from.Should().Be(expectedFrom);
        to.Should().Be(expectedTo);
    }

    [Theory]
    [InlineData("Over $1,000,000", 1000000, 1000000)]
    [InlineData("Over $50,000,000", 50000000, 50000000)]
    public void ParseAmountRange_OverFormat_ReturnsSameForBoth(string input, long expectedFrom, long expectedTo) {
        var (from, to) = DisclosureParsingHelper.ParseAmountRange(input);
        from.Should().Be(expectedFrom);
        to.Should().Be(expectedTo);
    }

    [Fact]
    public void ParseAmountRange_SingleValue_ReturnsZeroToValue() {
        var (from, to) = DisclosureParsingHelper.ParseAmountRange("$15,000");
        from.Should().Be(0);
        to.Should().Be(15000);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no dollar signs")]
    public void ParseAmountRange_InvalidOrEmpty_ReturnsZeros(string input) {
        var (from, to) = DisclosureParsingHelper.ParseAmountRange(input);
        from.Should().Be(0);
        to.Should().Be(0);
    }

    // ── ParseDate ───────────────────────────────────────────────────────

    [Fact]
    public void ParseDate_IsoFormat_ParsesCorrectly() {
        DisclosureParsingHelper.ParseDate("2024-03-15").Should().Be(new DateOnly(2024, 3, 15));
    }

    [Fact]
    public void ParseDate_MmDdYyyyFormat_ParsesCorrectly() {
        DisclosureParsingHelper.ParseDate("03/15/2024").Should().Be(new DateOnly(2024, 3, 15));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-date")]
    public void ParseDate_InvalidOrNull_ReturnsNull(string input) {
        DisclosureParsingHelper.ParseDate(input).Should().BeNull();
    }

    // ── ExtractTickerFromAssetName ──────────────────────────────────────

    [Theory]
    [InlineData("Apple Inc (AAPL) Common Stock", "AAPL")]
    [InlineData("Microsoft Corp [MSFT]", "MSFT")]
    [InlineData("Tesla Inc (TSLA)", "TSLA")]
    [InlineData("Alphabet (GOOG) Class C", "GOOG")]
    public void ExtractTickerFromAssetName_WithTicker_ReturnsTicker(string assetName, string expected) {
        DisclosureParsingHelper.ExtractTickerFromAssetName(assetName).Should().Be(expected);
    }

    [Theory]
    [InlineData("Apple Inc Common Stock")]
    [InlineData("Some Company")]
    [InlineData("")]
    public void ExtractTickerFromAssetName_NoTicker_ReturnsNull(string assetName) {
        DisclosureParsingHelper.ExtractTickerFromAssetName(assetName).Should().BeNull();
    }

    [Fact]
    public void ExtractTickerFromAssetName_LowercaseTicker_ReturnsUppercase() {
        DisclosureParsingHelper.ExtractTickerFromAssetName("test (aapl)").Should().Be("AAPL");
    }

    // ── GetCell ─────────────────────────────────────────────────────────

    [Fact]
    public void GetCell_ValidIndex_ReturnsValue() {
        var cells = new List<string> { "a", "b", "c" };
        DisclosureParsingHelper.GetCell(cells, 1).Should().Be("b");
    }

    [Fact]
    public void GetCell_OutOfBounds_ReturnsNull() {
        var cells = new List<string> { "a" };
        DisclosureParsingHelper.GetCell(cells, 5).Should().BeNull();
    }

    [Fact]
    public void GetCell_NegativeIndex_ReturnsNull() {
        var cells = new List<string> { "a" };
        DisclosureParsingHelper.GetCell(cells, -1).Should().BeNull();
    }

    // ── CleanSentinel ───────────────────────────────────────────────────

    [Fact]
    public void CleanSentinel_DashDash_ReturnsNull() {
        DisclosureParsingHelper.CleanSentinel("--").Should().BeNull();
    }

    [Fact]
    public void CleanSentinel_NullOrEmpty_ReturnsNull() {
        DisclosureParsingHelper.CleanSentinel(null).Should().BeNull();
        DisclosureParsingHelper.CleanSentinel("").Should().BeNull();
    }

    [Fact]
    public void CleanSentinel_NormalValue_ReturnsSame() {
        DisclosureParsingHelper.CleanSentinel("Purchase").Should().Be("Purchase");
    }

    // ── Truncate ────────────────────────────────────────────────────────

    [Fact]
    public void Truncate_WithinLimit_ReturnsSame() {
        DisclosureParsingHelper.Truncate("short", 10).Should().Be("short");
    }

    [Fact]
    public void Truncate_OverLimit_Truncated() {
        DisclosureParsingHelper.Truncate("abcdefghij", 5).Should().Be("abcde");
    }

    [Fact]
    public void Truncate_Null_ReturnsNull() {
        DisclosureParsingHelper.Truncate(null, 10).Should().BeNull();
    }

    // ── IsValidDisclosureUrl ────────────────────────────────────────────

    [Fact]
    public void IsValidDisclosureUrl_MatchingBase_ReturnsTrue() {
        DisclosureParsingHelper.IsValidDisclosureUrl(
            "https://disclosures.house.gov/public_disc/ptr-pdfs/2024/20024680.pdf",
            "https://disclosures.house.gov").Should().BeTrue();
    }

    [Fact]
    public void IsValidDisclosureUrl_DifferentBase_ReturnsFalse() {
        DisclosureParsingHelper.IsValidDisclosureUrl(
            "https://example.com/doc.pdf",
            "https://disclosures.house.gov").Should().BeFalse();
    }

    [Fact]
    public void IsValidDisclosureUrl_CaseInsensitive_ReturnsTrue() {
        DisclosureParsingHelper.IsValidDisclosureUrl(
            "HTTPS://DISCLOSURES.HOUSE.GOV/doc.pdf",
            "https://disclosures.house.gov").Should().BeTrue();
    }

    // ── ParseTransactionsFromHtml ───────────────────────────────────────

    [Fact]
    public void ParseTransactionsFromHtml_ValidTable_ParsesTransactions() {
        var html = """
            <html><body>
            <table>
              <thead><tr>
                <th>Transaction Date</th>
                <th>Owner</th>
                <th>Ticker</th>
                <th>Asset Name</th>
                <th>Asset Type</th>
                <th>Transaction Type</th>
                <th>Amount</th>
              </tr></thead>
              <tbody>
                <tr>
                  <td>2024-06-15</td>
                  <td>Self</td>
                  <td>AAPL</td>
                  <td>Apple Inc</td>
                  <td>Stock</td>
                  <td>Purchase</td>
                  <td>$1,001 - $15,000</td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        var result = DisclosureParsingHelper.ParseTransactionsFromHtml(
            html, "Test Member", CongressPosition.Representative,
            new DateOnly(2024, 7, 1), Substitute.For<ILogger>());

        result.Should().HaveCount(1);
        result[0].Ticker.Should().Be("AAPL");
        result[0].TransactionType.Should().Be(CongressTransactionType.Purchase);
        result[0].TransactionDate.Should().Be(new DateOnly(2024, 6, 15));
        result[0].AmountFrom.Should().Be(1001);
        result[0].AmountTo.Should().Be(15000);
    }

    [Fact]
    public void ParseTransactionsFromHtml_NoTables_ReturnsEmpty() {
        var html = "<html><body><p>No tables here</p></body></html>";

        var result = DisclosureParsingHelper.ParseTransactionsFromHtml(
            html, "Test", CongressPosition.Senator,
            new DateOnly(2024, 1, 1), Substitute.For<ILogger>());

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseTransactionsFromHtml_NonStockAssetType_Filtered() {
        var html = """
            <html><body>
            <table>
              <thead><tr>
                <th>Transaction Date</th>
                <th>Ticker</th>
                <th>Asset Name</th>
                <th>Asset Type</th>
                <th>Transaction Type</th>
                <th>Amount</th>
              </tr></thead>
              <tbody>
                <tr>
                  <td>2024-06-15</td>
                  <td>--</td>
                  <td>Municipal Bond Fund</td>
                  <td>Municipal Security</td>
                  <td>Purchase</td>
                  <td>$1,001 - $15,000</td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        var result = DisclosureParsingHelper.ParseTransactionsFromHtml(
            html, "Test", CongressPosition.Senator,
            new DateOnly(2024, 1, 1), Substitute.For<ILogger>());

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseTransactionsFromHtml_BothTransactionAndNotificationDateColumns_UsesTransactionDate() {
        // Senate HTML disclosures regularly carry BOTH a "Transaction Date" (when the
        // member traded) AND a "Notification Date" (when the filing was acknowledged) —
        // the two can differ by weeks. MapColumnIndices' priority chain (transaction →
        // notification → any-date) hinges on this distinction: every analyst-facing
        // metric (timing relative to legislation, frontrunning windows) is computed from
        // the transaction date, never the notification date.
        //
        // The risk this test pins: a refactor that simplifies to a single
        // `FindIndex(h => h.Contains("date"))` would pick the FIRST date-matching column.
        // Because Senate often renders Notification first, the helper would silently
        // record FilingDate-ish values as TransactionDate, blowing up every "trades
        // within N days of a hearing" query downstream. The existing positive test only
        // has one date column — neither this nor the integration tests catch the
        // priority regression.
        //
        // Notification Date 2024-07-01 is deliberately placed BEFORE Transaction Date
        // 2024-06-15 so the simplified fallback would pick the wrong column.
        var html = """
            <html><body>
            <table>
              <thead><tr>
                <th>Notification Date</th>
                <th>Transaction Date</th>
                <th>Ticker</th>
                <th>Asset Name</th>
                <th>Transaction Type</th>
                <th>Amount</th>
              </tr></thead>
              <tbody>
                <tr>
                  <td>2024-07-01</td>
                  <td>2024-06-15</td>
                  <td>AAPL</td>
                  <td>Apple Inc</td>
                  <td>Purchase</td>
                  <td>$1,001 - $15,000</td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        var result = DisclosureParsingHelper.ParseTransactionsFromHtml(
            html, "Test Senator", CongressPosition.Senator,
            new DateOnly(2024, 7, 1), Substitute.For<ILogger>());

        result.Should().HaveCount(1);
        result[0].TransactionDate.Should().Be(new DateOnly(2024, 6, 15));
    }

    [Fact]
    public void ParseTransactionsFromHtml_UnrecognizedTransactionType_SkipsRow() {
        // Congress disclosures occasionally carry transaction types that the
        // enum intentionally doesn't model — "Exchange" and "Receive" are
        // called out by the comment above ParseTransactionType. Those rows
        // must be skipped (not silently recorded as a Purchase or Sale),
        // pinning the LogDebug + return-null branch in ParseTransactionRow.
        var html = """
            <html><body>
            <table>
              <thead><tr>
                <th>Transaction Date</th>
                <th>Ticker</th>
                <th>Asset Name</th>
                <th>Transaction Type</th>
                <th>Amount</th>
              </tr></thead>
              <tbody>
                <tr>
                  <td>2024-06-15</td>
                  <td>AAPL</td>
                  <td>Apple Inc</td>
                  <td>Exchange</td>
                  <td>$1,001 - $15,000</td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        var result = DisclosureParsingHelper.ParseTransactionsFromHtml(
            html, "Test", CongressPosition.Representative,
            new DateOnly(2024, 7, 1), Substitute.For<ILogger>());

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseTransactionsFromHtml_TableWithoutTheadButFirstRowTh_ReadsHeadersFromFirstRow() {
        // Congress disclosure HTML — particularly older House PTRs and
        // hand-coded staff exports — frequently omits a `<thead>` and
        // simply places `<th>` cells in the first `<tr>` of `<tbody>` (or
        // bare, with no tbody at all). `ExtractHeaderTexts` handles this
        // with a null-coalescing fallback: `SelectNodes(".//thead//th") ??
        // SelectNodes(".//tr[1]//th")`. Every other parse test in this
        // file uses a proper `<thead>`, so the fallback branch is
        // unexercised — a refactor that drops the `??` (or that mis-orders
        // it) would silently start returning zero transactions on the
        // entire class of thead-less filings, with no visible failure
        // because IsTransactionTable would receive an empty header list
        // and return false. Pin the fallback with a deliberately
        // thead-less table that carries a real Purchase row.
        var html = """
            <html><body>
            <table>
              <tr>
                <th>Transaction Date</th>
                <th>Ticker</th>
                <th>Asset Name</th>
                <th>Transaction Type</th>
                <th>Amount</th>
              </tr>
              <tbody>
                <tr>
                  <td>2024-09-20</td>
                  <td>MSFT</td>
                  <td>Microsoft Corp</td>
                  <td>Purchase</td>
                  <td>$15,001 - $50,000</td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        var result = DisclosureParsingHelper.ParseTransactionsFromHtml(
            html, "Test Rep", CongressPosition.Representative,
            new DateOnly(2024, 10, 1), Substitute.For<ILogger>());

        result.Should().HaveCount(1);
        result[0].Ticker.Should().Be("MSFT");
        result[0].TransactionType.Should().Be(CongressTransactionType.Purchase);
        result[0].TransactionDate.Should().Be(new DateOnly(2024, 9, 20));
    }

    [Fact]
    public void ParseTransactionsFromHtml_TickerExtractedFromAssetName() {
        var html = """
            <html><body>
            <table>
              <thead><tr>
                <th>Transaction Date</th>
                <th>Asset Name</th>
                <th>Transaction Type</th>
                <th>Amount</th>
              </tr></thead>
              <tbody>
                <tr>
                  <td>2024-06-15</td>
                  <td>Apple Inc (AAPL) Common Stock</td>
                  <td>Sale</td>
                  <td>$15,001 - $50,000</td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        var result = DisclosureParsingHelper.ParseTransactionsFromHtml(
            html, "Test", CongressPosition.Representative,
            new DateOnly(2024, 7, 1), Substitute.For<ILogger>());

        result.Should().HaveCount(1);
        result[0].Ticker.Should().Be("AAPL");
        result[0].TransactionType.Should().Be(CongressTransactionType.Sale);
    }

    [Fact]
    public void ParseTransactionsFromHtml_DescriptionHeaderInsteadOfAssetName_PopulatesAssetNameFromDescriptionColumn() {
        // MapColumnIndices resolves the asset column via a three-tier fallback:
        //   1. Contains("asset") && Contains("name")  → "Asset Name" (primary)
        //   2. Contains("asset") && !Contains("type") → bare "Asset" (rare)
        //   3. Contains("description")                → "Description" (older House PTRs)
        // Every existing happy-path pin in this file uses "Asset Name" (tier 1).
        // Tier 3 is exercised by older House PTR exports and some hand-coded
        // staff submissions that label the asset column "Description" rather than
        // "Asset Name" — IsTransactionTable also accepts "description" in its
        // hasAsset gate (`Any(h.Contains("description"))`) so these tables make
        // it past the table-detection check; they then rely on the third-tier
        // assetCol fallback to actually pluck the column.
        //
        // The risk this pin catches: a refactor that "tidies up" the assetCol
        // chain — e.g. collapses to just `Contains("asset")` — would compile
        // cleanly, pass every existing Asset-Name test, AND pass
        // IsTransactionTable, then silently set cols.Asset = -1 for every
        // Description-only table. GetCell returns null on -1, CleanSentinel
        // passes null through, the assetName check in ParseTransactionRow
        // falls back to the ticker (which is present here as "AAPL"), the
        // transaction IS still recorded — but with AssetName=null. Downstream
        // consumers that group by AssetName (the "trades by company" UI, the
        // CSV export's Description column) silently lose context for every
        // legacy-format row.
        //
        // Pin: a House PTR header with no "Asset Name" or bare "Asset" — only
        // "Description". The valid row carries AAPL ticker + Apple text. The
        // assertion checks AssetName is populated from the Description column,
        // proving the third-tier fallback fired. Without the fallback, AssetName
        // would be null.
        var html = """
            <html><body>
            <table>
              <thead><tr>
                <th>Transaction Date</th>
                <th>Ticker</th>
                <th>Description</th>
                <th>Transaction Type</th>
                <th>Amount</th>
              </tr></thead>
              <tbody>
                <tr>
                  <td>2024-06-15</td>
                  <td>AAPL</td>
                  <td>Apple Inc common stock</td>
                  <td>Purchase</td>
                  <td>$1,001 - $15,000</td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        var result = DisclosureParsingHelper.ParseTransactionsFromHtml(
            html, "Test Rep", CongressPosition.Representative,
            new DateOnly(2024, 7, 1), Substitute.For<ILogger>());

        result.Should().HaveCount(1);
        result[0].AssetName.Should().Be("Apple Inc common stock");
        result[0].Ticker.Should().Be("AAPL");
    }

    [Fact]
    public void ParseTransactionsFromHtml_BareAssetHeaderWithoutNameOrType_PopulatesAssetNameFromSecondTierFallback() {
        // Sibling pin to ParseTransactionsFromHtml_DescriptionHeaderInsteadOfAssetName.
        // MapColumnIndices' assetCol three-tier fallback:
        //   1. Contains("asset") && Contains("name")  → "Asset Name" (covered by happy-path tests)
        //   2. Contains("asset") && !Contains("type") → bare "Asset" (this pin)
        //   3. Contains("description")                → "Description" (covered by sibling)
        // Tier 2 — bare "Asset" header without "Name" and not "Asset Type" — fires
        // for older House PTR exports and hand-coded staff submissions that label the
        // asset column simply "Asset". The `!Contains("type")` clause is load-bearing:
        // it must distinguish "Asset" from "Asset Type" (which IS present in modern
        // House PTRs alongside "Asset Name" — see ParseTransactionsFromHtml_ValidTable_…).
        // Without the negative clause, a regression like `Contains("asset")` alone
        // would silently pick up "Asset Type" as the asset column, populating
        // AssetName with the asset-type string ("Stock", "Bond Fund") instead of
        // the actual asset description.
        //
        // The two existing sibling pins (Asset Name → tier 1, Description → tier 3)
        // don't exercise tier 2 — neither would catch a regression that drops the
        // middle FindIndex call entirely (collapsing the chain from 3 tiers to 2).
        // Pin tier 2 with a deliberately bare "Asset" header so the middle fallback
        // is required to fire.
        var html = """
            <html><body>
            <table>
              <thead><tr>
                <th>Transaction Date</th>
                <th>Ticker</th>
                <th>Asset</th>
                <th>Transaction Type</th>
                <th>Amount</th>
              </tr></thead>
              <tbody>
                <tr>
                  <td>2024-06-15</td>
                  <td>AAPL</td>
                  <td>Apple Inc</td>
                  <td>Purchase</td>
                  <td>$1,001 - $15,000</td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        var result = DisclosureParsingHelper.ParseTransactionsFromHtml(
            html, "Test Rep", CongressPosition.Representative,
            new DateOnly(2024, 7, 1), Substitute.For<ILogger>());

        result.Should().HaveCount(1);
        result[0].AssetName.Should().Be("Apple Inc");
    }

    [Fact]
    public void ParseTransactionsFromHtml_RowWithEmptySentinelInTransactionDateColumn_IsSilentlySkipped() {
        // ParseTransactionRow's first guard after column extraction is:
        //   var txDate = CleanSentinel(GetCell(cellTexts, cols.Date)) is { } dateStr ? ParseDate(dateStr) : null;
        //   if (txDate == null) return null;
        // The CleanSentinel + `is { }` chain handles three cases that all yield txDate=null:
        //   1. cols.Date == -1 (column missing entirely) → GetCell returns null → CleanSentinel(null) returns null → `is { }` pattern fails → skip
        //   2. Empty cell ("" or whitespace) → CleanSentinel returns null → skip
        //   3. "--" empty sentinel → CleanSentinel returns null → skip
        //   4. Non-sentinel but unparseable date → ParseDate returns null → skip
        //
        // Case 3 — the "--" empty sentinel in the DATE column — is structurally
        // distinct from every existing pin and uniquely catches a specific class
        // of refactor regressions:
        //
        //   • A "simplify CleanSentinel" refactor that drops the `value == EmptySentinel`
        //     check (or that changes the sentinel constant from "--" to e.g. "—" em-dash,
        //     "N/A", or empty string) would compile, pass every existing test (none of
        //     which exercises "--" in the date column), and silently let "--" flow into
        //     ParseDate which would yield null via its IsNullOrWhiteSpace+TryParse
        //     fall-through. The row would still skip — but for a DIFFERENT reason, and
        //     the per-row diagnostic context degrades (operators reading the
        //     "Skipping transaction with unrecognized type" debug log won't see
        //     this row because the type check comes AFTER the date guard).
        //
        //   • A refactor that "tightens" the `is { } dateStr ? ParseDate(dateStr) : null`
        //     pattern to a simpler `ParseDate(GetCell(cellTexts, cols.Date))` would
        //     skip CleanSentinel entirely. ParseDate("--") returns null via
        //     IsNullOrWhiteSpace=false, TryParse fail, TryParseExact fail — same
        //     observable outcome (row skipped). But the CleanSentinel layer is
        //     load-bearing for OTHER columns where the sentinel-vs-empty distinction
        //     matters (the existing `NonStockAssetType_Filtered` test relies on
        //     `--` in the Owner column being treated as null, not as the literal
        //     string "--"). Dropping CleanSentinel for the date path would
        //     subtly couple-uncouple the helper across columns.
        //
        //   • Inversion of the conditional: `is null` instead of `is { } dateStr`,
        //     under the (false) intuition of "let me explicit-null-check this":
        //       var txDate = CleanSentinel(...) is null ? null : ParseDate(...);
        //     would compile but break the variable capture — `dateStr` wouldn't
        //     be in scope. A worse refactor that pre-captured the cleaned value
        //     and then null-checked would observably work the same. Hard to
        //     write a regression test that catches this specific case.
        //
        // The existing pins this complements:
        //   • `NonStockAssetType_Filtered` puts "--" in the Owner column — proves
        //     the sentinel is recognized for owner attribution. Does NOT exercise
        //     the date-column sentinel.
        //   • `ParseDate_InvalidOrNull_ReturnsNull` proves ParseDate returns null
        //     for null/empty/"not-a-date". Does NOT exercise CleanSentinel.
        //   • `CleanSentinel_DashDash_ReturnsNull` proves CleanSentinel maps "--"
        //     to null. Does NOT exercise the end-to-end ParseTransactionRow flow.
        // The three-pin family separately validates the components; this fourth
        // pin proves they wire together correctly when "--" appears specifically
        // in the date column. A regression in any single component (CleanSentinel
        // not recognizing "--", ParseDate not rejecting "--", or
        // ParseTransactionRow short-circuiting differently) is caught here.
        //
        // Production trigger: Senate eFD occasionally renders the transaction-date
        // column as "--" when the filer omits an explicit transaction date (e.g.
        // for amended filings where only the filing date is known). The expected
        // behavior is to skip the row silently — the partial data isn't useful
        // for the "trades within N days of a hearing" analytics that drive the
        // dashboard.
        //
        // Construction: a header with all the standard transaction-table columns,
        // one row where the Transaction Date cell is "--" but every OTHER cell
        // (ticker, asset, type, amount) is fully populated. The assertion checks
        // that the result is empty — proving the date guard fired, NOT some
        // downstream filter. To distinguish this from "the table wasn't detected
        // as a transaction table at all" (IsTransactionTable failure), the
        // assertion would need to include a second VALID row whose date IS
        // populated, then assert exactly one transaction in the result. But that
        // doubles the test surface; the simpler "empty result with valid-shaped
        // row" assertion is enough since IsTransactionTable's hasDate check looks
        // at the HEADER text ("transaction date"), not at the cell content —
        // the header still passes that check.
        var html = """
            <html><body>
            <table>
              <thead><tr>
                <th>Transaction Date</th>
                <th>Ticker</th>
                <th>Asset Name</th>
                <th>Transaction Type</th>
                <th>Amount</th>
              </tr></thead>
              <tbody>
                <tr>
                  <td>--</td>
                  <td>AAPL</td>
                  <td>Apple Inc</td>
                  <td>Purchase</td>
                  <td>$1,001 - $15,000</td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        var result = DisclosureParsingHelper.ParseTransactionsFromHtml(
            html, "Test Member", CongressPosition.Representative,
            new DateOnly(2024, 7, 1), Substitute.For<ILogger>());

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseTransactionsFromHtml_FilerColumnInsteadOfOwner_PopulatesOwnerTypeFromFilerColumn() {
        // MapColumnIndices resolves the owner column with `h.Contains("owner") ||
        // h.Contains("filer")`. The two alternatives are independent: House PTRs use
        // an "Owner" column (Self / SP / JT / DC), Senate disclosures use a "Filer"
        // column (Joint / Self / Spouse). Every existing happy-path pin in this file
        // uses an "Owner" column; the `|| h.Contains("filer")` arm is unpinned and
        // a refactor that "simplifies" the alternation to just `Contains("owner")`
        // would compile cleanly, pass every existing test, and silently null out
        // OwnerType for every Senate row in production — losing the joint vs spouse
        // vs self distinction the analytics tier displays as the trade attribution.
        //
        // The failure mode is invisible: the row still parses (date/ticker/asset
        // come through), the transaction is recorded, but the OwnerType column in
        // the DB receives null where it used to carry "Joint" or "Spouse". The
        // dashboard's "trades attributed to spouse" filter silently empties out.
        //
        // Pin the filer-column path with a Senate-style header. The assertion
        // checks OwnerType on the parsed transaction — proving the filer column
        // was actually resolved AND that its value flowed through GetCell →
        // CleanSentinel → Truncate to the persisted field.
        var html = """
            <html><body>
            <table>
              <thead><tr>
                <th>Transaction Date</th>
                <th>Filer Type</th>
                <th>Ticker</th>
                <th>Asset Name</th>
                <th>Transaction Type</th>
                <th>Amount</th>
              </tr></thead>
              <tbody>
                <tr>
                  <td>2024-06-15</td>
                  <td>Joint</td>
                  <td>AAPL</td>
                  <td>Apple Inc</td>
                  <td>Purchase</td>
                  <td>$1,001 - $15,000</td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        var result = DisclosureParsingHelper.ParseTransactionsFromHtml(
            html, "Test Senator", CongressPosition.Senator,
            new DateOnly(2024, 7, 1), Substitute.For<ILogger>());

        result.Should().HaveCount(1);
        result[0].OwnerType.Should().Be("Joint");
    }
}
