using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperTests
{
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
    public void ParseTransactionType_KnownTypes_ReturnsExpected(
        string input,
        CongressTransactionType expected
    )
    {
        DisclosureParsingHelper.ParseTransactionType(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("S(full)", CongressTransactionType.Sale)]
    [InlineData("P(partial)", CongressTransactionType.Purchase)]
    public void ParseTransactionType_AbbreviationWithParenNoSpace_ReturnsExpected(
        string input,
        CongressTransactionType expected
    )
    {
        DisclosureParsingHelper.ParseTransactionType(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Exchange")]
    [InlineData("Receive")]
    [InlineData("unknown")]
    public void ParseTransactionType_UnknownOrNull_ReturnsNull(string input)
    {
        DisclosureParsingHelper.ParseTransactionType(input).Should().BeNull();
    }

    // ── ParseAmountRange ────────────────────────────────────────────────

    [Theory]
    [InlineData("$1,001 - $15,000", 1001, 15000)]
    [InlineData("$50,001 - $100,000", 50001, 100000)]
    [InlineData("$15,001 - $50,000", 15001, 50000)]
    public void ParseAmountRange_TwoValues_ReturnsFromTo(
        string input,
        long expectedFrom,
        long expectedTo
    )
    {
        var (from, to) = DisclosureParsingHelper.ParseAmountRange(input);
        from.Should().Be(expectedFrom);
        to.Should().Be(expectedTo);
    }

    [Theory]
    [InlineData("Over $1,000,000", 1000000, 1000000)]
    [InlineData("Over $50,000,000", 50000000, 50000000)]
    public void ParseAmountRange_OverFormat_ReturnsSameForBoth(
        string input,
        long expectedFrom,
        long expectedTo
    )
    {
        var (from, to) = DisclosureParsingHelper.ParseAmountRange(input);
        from.Should().Be(expectedFrom);
        to.Should().Be(expectedTo);
    }

    [Fact]
    public void ParseAmountRange_SingleValue_ReturnsZeroToValue()
    {
        var (from, to) = DisclosureParsingHelper.ParseAmountRange("$15,000");
        from.Should().Be(0);
        to.Should().Be(15000);
    }

    [Fact]
    public void ParseAmountRange_UnderAmount_ReturnsUpperBound()
    {
        var (from, to) = DisclosureParsingHelper.ParseAmountRange("Under $1,000");
        from.Should().Be(0);
        to.Should().Be(1000);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no dollar signs")]
    public void ParseAmountRange_InvalidOrEmpty_ReturnsZeros(string input)
    {
        var (from, to) = DisclosureParsingHelper.ParseAmountRange(input);
        from.Should().Be(0);
        to.Should().Be(0);
    }

    // ── ParseDate ───────────────────────────────────────────────────────

    [Fact]
    public void ParseDate_IsoFormat_ParsesCorrectly()
    {
        DisclosureParsingHelper.ParseDate("2024-03-15").Should().Be(new DateOnly(2024, 3, 15));
    }

    [Fact]
    public void ParseDate_MmDdYyyyFormat_ParsesCorrectly()
    {
        DisclosureParsingHelper.ParseDate("03/15/2024").Should().Be(new DateOnly(2024, 3, 15));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-date")]
    public void ParseDate_InvalidOrNull_ReturnsNull(string input)
    {
        DisclosureParsingHelper.ParseDate(input).Should().BeNull();
    }

    // ── ExtractTickerFromAssetName ──────────────────────────────────────

    [Theory]
    [InlineData("Apple Inc (AAPL) Common Stock", "AAPL")]
    [InlineData("Microsoft Corp [MSFT]", "MSFT")]
    [InlineData("Tesla Inc (TSLA)", "TSLA")]
    [InlineData("Alphabet (GOOG) Class C", "GOOG")]
    public void ExtractTickerFromAssetName_WithTicker_ReturnsTicker(
        string assetName,
        string expected
    )
    {
        DisclosureParsingHelper.ExtractTickerFromAssetName(assetName).Should().Be(expected);
    }

    [Theory]
    [InlineData("Apple Inc Common Stock")]
    [InlineData("Some Company")]
    [InlineData("")]
    public void ExtractTickerFromAssetName_NoTicker_ReturnsNull(string assetName)
    {
        DisclosureParsingHelper.ExtractTickerFromAssetName(assetName).Should().BeNull();
    }

    [Fact]
    public void ExtractTickerFromAssetName_LowercaseTicker_ReturnsUppercase()
    {
        DisclosureParsingHelper.ExtractTickerFromAssetName("test (aapl)").Should().Be("AAPL");
    }

    // ── GetCell ─────────────────────────────────────────────────────────

    [Fact]
    public void GetCell_ValidIndex_ReturnsValue()
    {
        var cells = new List<string> { "a", "b", "c" };
        DisclosureParsingHelper.GetCell(cells, 1).Should().Be("b");
    }

    [Fact]
    public void GetCell_OutOfBounds_ReturnsNull()
    {
        var cells = new List<string> { "a" };
        DisclosureParsingHelper.GetCell(cells, 5).Should().BeNull();
    }

    [Fact]
    public void GetCell_NegativeIndex_ReturnsNull()
    {
        var cells = new List<string> { "a" };
        DisclosureParsingHelper.GetCell(cells, -1).Should().BeNull();
    }

    // ── CleanSentinel ───────────────────────────────────────────────────

    [Fact]
    public void CleanSentinel_DashDash_ReturnsNull()
    {
        DisclosureParsingHelper.CleanSentinel("--").Should().BeNull();
    }

    [Fact]
    public void CleanSentinel_NullOrEmpty_ReturnsNull()
    {
        DisclosureParsingHelper.CleanSentinel(null).Should().BeNull();
        DisclosureParsingHelper.CleanSentinel("").Should().BeNull();
    }

    [Fact]
    public void CleanSentinel_NormalValue_ReturnsSame()
    {
        DisclosureParsingHelper.CleanSentinel("Purchase").Should().Be("Purchase");
    }

    // ── Truncate ────────────────────────────────────────────────────────

    [Fact]
    public void Truncate_WithinLimit_ReturnsSame()
    {
        DisclosureParsingHelper.Truncate("short", 10).Should().Be("short");
    }

    [Fact]
    public void Truncate_ValueExactlyMaxLength_ReturnsOriginal()
    {
        DisclosureParsingHelper.Truncate("abc", 3).Should().Be("abc");
    }

    [Fact]
    public void Truncate_OverLimit_Truncated()
    {
        DisclosureParsingHelper.Truncate("abcdefghij", 5).Should().Be("abcde");
    }

    [Fact]
    public void Truncate_Null_ReturnsNull()
    {
        DisclosureParsingHelper.Truncate(null, 10).Should().BeNull();
    }

    [Fact]
    public void Truncate_EmptyString_ReturnsEmpty()
    {
        DisclosureParsingHelper.Truncate("", 5).Should().Be("");
    }

    [Fact]
    public void Truncate_LowSurrogateAtBoundary_TruncatesNormally()
    {
        // "🏛" (U+1F3DB) is a surrogate pair: high \uD83C at index 0, low \uDFDB at index 1.
        // maxLength=2 lands AFTER the low surrogate — the full pair fits, so the
        // surrogate guard must NOT back up. This is the inverse of the sibling test
        // in TruncateSurrogatePairTests (which lands ON the high surrogate).
        var input = "🏛trailing"; // length = 2 + 8 = 10
        var result = DisclosureParsingHelper.Truncate(input, 2);

        result.Should().Be("🏛");
    }

    // ── IsValidDisclosureUrl ────────────────────────────────────────────

    [Fact]
    public void IsValidDisclosureUrl_MatchingBase_ReturnsTrue()
    {
        DisclosureParsingHelper
            .IsValidDisclosureUrl(
                "https://disclosures.house.gov/public_disc/ptr-pdfs/2024/20024680.pdf",
                "https://disclosures.house.gov"
            )
            .Should()
            .BeTrue();
    }

    [Fact]
    public void IsValidDisclosureUrl_DifferentBase_ReturnsFalse()
    {
        DisclosureParsingHelper
            .IsValidDisclosureUrl("https://example.com/doc.pdf", "https://disclosures.house.gov")
            .Should()
            .BeFalse();
    }

    [Fact]
    public void IsValidDisclosureUrl_CaseInsensitive_ReturnsTrue()
    {
        DisclosureParsingHelper
            .IsValidDisclosureUrl(
                "HTTPS://DISCLOSURES.HOUSE.GOV/doc.pdf",
                "https://disclosures.house.gov"
            )
            .Should()
            .BeTrue();
    }

    // ── ParseTransactionsFromHtml ───────────────────────────────────────

    [Fact]
    public void ParseTransactionsFromHtml_ValidTable_ParsesTransactions()
    {
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
            html,
            "Test Member",
            CongressPosition.Representative,
            new DateOnly(2024, 7, 1),
            Substitute.For<ILogger>()
        );

        result.Should().HaveCount(1);
        result[0].Ticker.Should().Be("AAPL");
        result[0].TransactionType.Should().Be(CongressTransactionType.Purchase);
        result[0].TransactionDate.Should().Be(new DateOnly(2024, 6, 15));
        result[0].AmountFrom.Should().Be(1001);
        result[0].AmountTo.Should().Be(15000);
    }

    [Fact]
    public void ParseTransactionsFromHtml_NoTables_ReturnsEmpty()
    {
        var html = "<html><body><p>No tables here</p></body></html>";

        var result = DisclosureParsingHelper.ParseTransactionsFromHtml(
            html,
            "Test",
            CongressPosition.Senator,
            new DateOnly(2024, 1, 1),
            Substitute.For<ILogger>()
        );

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseTransactionsFromHtml_NonStockAssetType_Filtered()
    {
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
            html,
            "Test",
            CongressPosition.Senator,
            new DateOnly(2024, 1, 1),
            Substitute.For<ILogger>()
        );

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseTransactionsFromHtml_BothTransactionAndNotificationDateColumns_UsesTransactionDate()
    {
        // Pins MapColumnIndices' date-column priority: "Transaction Date" wins over
        // "Notification Date". Notification placed first to catch regressions that
        // simplify to a single Contains("date") match.
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
            html,
            "Test Senator",
            CongressPosition.Senator,
            new DateOnly(2024, 7, 1),
            Substitute.For<ILogger>()
        );

        result.Should().HaveCount(1);
        result[0].TransactionDate.Should().Be(new DateOnly(2024, 6, 15));
    }

    [Fact]
    public void ParseTransactionsFromHtml_UnrecognizedTransactionType_SkipsRow()
    {
        // "Exchange"/"Receive" are intentionally unmodeled — rows must be
        // skipped, not silently recorded as Purchase/Sale.
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
            html,
            "Test",
            CongressPosition.Representative,
            new DateOnly(2024, 7, 1),
            Substitute.For<ILogger>()
        );

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseTransactionsFromHtml_TableWithoutTheadButFirstRowTh_ReadsHeadersFromFirstRow()
    {
        // Pins ExtractHeaderTexts' fallback: SelectNodes(".//tr[1]//th")
        // when no <thead> exists. Older House PTRs use this layout.
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
            html,
            "Test Rep",
            CongressPosition.Representative,
            new DateOnly(2024, 10, 1),
            Substitute.For<ILogger>()
        );

        result.Should().HaveCount(1);
        result[0].Ticker.Should().Be("MSFT");
        result[0].TransactionType.Should().Be(CongressTransactionType.Purchase);
        result[0].TransactionDate.Should().Be(new DateOnly(2024, 9, 20));
    }

    [Fact]
    public void ParseTransactionsFromHtml_TickerExtractedFromAssetName()
    {
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
            html,
            "Test",
            CongressPosition.Representative,
            new DateOnly(2024, 7, 1),
            Substitute.For<ILogger>()
        );

        result.Should().HaveCount(1);
        result[0].Ticker.Should().Be("AAPL");
        result[0].TransactionType.Should().Be(CongressTransactionType.Sale);
    }

    [Fact]
    public void ParseTransactionsFromHtml_DescriptionHeaderInsteadOfAssetName_PopulatesAssetNameFromDescriptionColumn()
    {
        // Pins assetCol tier-3 fallback: Contains("description"). Older House
        // PTRs label the asset column "Description" instead of "Asset Name".
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
            html,
            "Test Rep",
            CongressPosition.Representative,
            new DateOnly(2024, 7, 1),
            Substitute.For<ILogger>()
        );

        result.Should().HaveCount(1);
        result[0].AssetName.Should().Be("Apple Inc common stock");
        result[0].Ticker.Should().Be("AAPL");
    }

    [Fact]
    public void ParseTransactionsFromHtml_BareAssetHeaderWithoutNameOrType_PopulatesAssetNameFromSecondTierFallback()
    {
        // Pins assetCol tier-2 fallback: Contains("asset") && !Contains("type").
        // Bare "Asset" header must resolve correctly and not collide with "Asset Type".
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
            html,
            "Test Rep",
            CongressPosition.Representative,
            new DateOnly(2024, 7, 1),
            Substitute.For<ILogger>()
        );

        result.Should().HaveCount(1);
        result[0].AssetName.Should().Be("Apple Inc");
    }

    [Fact]
    public void ParseTransactionsFromHtml_RowWithEmptySentinelInTransactionDateColumn_IsSilentlySkipped()
    {
        // Pins the CleanSentinel → ParseDate → null → skip chain when the
        // date cell is "--". Senate eFD uses this for amended filings where
        // only the filing date is known.
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
            html,
            "Test Member",
            CongressPosition.Representative,
            new DateOnly(2024, 7, 1),
            Substitute.For<ILogger>()
        );

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseTransactionsFromHtml_FilerColumnInsteadOfOwner_PopulatesOwnerTypeFromFilerColumn()
    {
        // Pins ownerCol's Contains("filer") fallback — Senate uses "Filer Type"
        // instead of "Owner". Without this arm, OwnerType silently nulls out.
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
            html,
            "Test Senator",
            CongressPosition.Senator,
            new DateOnly(2024, 7, 1),
            Substitute.For<ILogger>()
        );

        result.Should().HaveCount(1);
        result[0].OwnerType.Should().Be("Joint");
    }

    // ── Partial-branch fills ────────────────────────────────────────────

    [Fact]
    public void ParseTransactionsFromHtml_TableWithNoTheadAndNoLeadingThRow_SkipsTableSilently()
    {
        // Pins ExtractHeaderTexts returning null when both thead//th and
        // tr[1]//th selectors miss — prevents NRE in IsTransactionTable.
        var html = """
            <html><body>
            <table>
              <tbody>
                <tr>
                  <td>2024-06-15</td>
                  <td>AAPL</td>
                  <td>Purchase</td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        var result = DisclosureParsingHelper.ParseTransactionsFromHtml(
            html,
            "Test",
            CongressPosition.Representative,
            new DateOnly(2024, 7, 1),
            Substitute.For<ILogger>()
        );

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseTransactionsFromHtml_OnlyNotificationDateColumnNoTransactionDate_UsesNotificationDateAsTxDate()
    {
        // Pins dateCol tier-2 fallback: "Notification Date" when no
        // "Transaction Date" column exists.
        var html = """
            <html><body>
            <table>
              <thead><tr>
                <th>Notification Date</th>
                <th>Ticker</th>
                <th>Asset Name</th>
                <th>Transaction Type</th>
                <th>Amount</th>
              </tr></thead>
              <tbody>
                <tr>
                  <td>2024-07-01</td>
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
            html,
            "Test",
            CongressPosition.Senator,
            new DateOnly(2024, 7, 5),
            Substitute.For<ILogger>()
        );

        result.Should().HaveCount(1);
        result[0].TransactionDate.Should().Be(new DateOnly(2024, 7, 1));
    }

    [Fact]
    public void ParseTransactionsFromHtml_OnlyGenericDateColumn_UsesItAsTxDate()
    {
        // Pins dateCol tier-3 fallback: bare "Date" header (legacy tables).
        var html = """
            <html><body>
            <table>
              <thead><tr>
                <th>Date</th>
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
                  <td>Purchase</td>
                  <td>$1,001 - $15,000</td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        var result = DisclosureParsingHelper.ParseTransactionsFromHtml(
            html,
            "Test",
            CongressPosition.Representative,
            new DateOnly(2024, 7, 1),
            Substitute.For<ILogger>()
        );

        result.Should().HaveCount(1);
        result[0].TransactionDate.Should().Be(new DateOnly(2024, 6, 15));
    }

    [Fact]
    public void ParseTransactionsFromHtml_BareTypeHeaderWithoutTransactionPrefix_ResolvesTypeColumn()
    {
        // Pins typeCol fallback: exact "type" match (not "Asset Type").
        var html = """
            <html><body>
            <table>
              <thead><tr>
                <th>Transaction Date</th>
                <th>Ticker</th>
                <th>Asset Name</th>
                <th>Type</th>
                <th>Amount</th>
              </tr></thead>
              <tbody>
                <tr>
                  <td>2024-06-15</td>
                  <td>AAPL</td>
                  <td>Apple Inc</td>
                  <td>Sale</td>
                  <td>$1,001 - $15,000</td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        var result = DisclosureParsingHelper.ParseTransactionsFromHtml(
            html,
            "Test",
            CongressPosition.Representative,
            new DateOnly(2024, 7, 1),
            Substitute.For<ILogger>()
        );

        result.Should().HaveCount(1);
        result[0].TransactionType.Should().Be(CongressTransactionType.Sale);
    }

    [Fact]
    public void ParseTransactionsFromHtml_RowWithNoCellElements_IsSilentlySkipped()
    {
        // Pins ParseTransactionRow's null guard on SelectNodes(".//td") — a
        // <tr> with only <th> cells must not NRE.
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
                <tr><th>Section header — no td cells</th></tr>
              </tbody>
            </table>
            </body></html>
            """;

        var result = DisclosureParsingHelper.ParseTransactionsFromHtml(
            html,
            "Test",
            CongressPosition.Representative,
            new DateOnly(2024, 7, 1),
            Substitute.For<ILogger>()
        );

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseTransactionsFromHtml_RowWithValidDateButEmptyTickerAndEmptyAsset_IsSkipped()
    {
        // Pins both-empty guard: ticker + assetName both null/empty → skip row.
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
                  <td></td>
                  <td></td>
                  <td>Purchase</td>
                  <td>$1,001 - $15,000</td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        var result = DisclosureParsingHelper.ParseTransactionsFromHtml(
            html,
            "Test",
            CongressPosition.Representative,
            new DateOnly(2024, 7, 1),
            Substitute.For<ILogger>()
        );

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseTransactionsFromHtml_AssetNameWithoutParenthesizedTicker_RecordsTransactionWithNullTicker()
    {
        // Pins ticker?.ToUpperInvariant() null path — asset present but no
        // parenthesized ticker and no ticker column value.
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
                  <td></td>
                  <td>Apple Inc common stock with no ticker symbol</td>
                  <td>Purchase</td>
                  <td>$1,001 - $15,000</td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        var result = DisclosureParsingHelper.ParseTransactionsFromHtml(
            html,
            "Test",
            CongressPosition.Representative,
            new DateOnly(2024, 7, 1),
            Substitute.For<ILogger>()
        );

        result.Should().HaveCount(1);
        result[0].Ticker.Should().BeNull();
        result[0].AssetName.Should().Be("Apple Inc common stock with no ticker symbol");
    }
}
