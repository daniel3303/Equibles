using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// A page break reprints the PTR column-header block, and the word clustering can land it
/// anywhere: merged into a row's own visual line, or as a standalone line splitting a row
/// from its wrapped remainder. The line-level header guard (#3378) only covers the standalone
/// case — and even there it CUT OFF the wrapped remainder instead of flowing past the header.
/// Production stored 707 asset names carrying the verbatim header block and 49 impossible
/// amount brackets (e.g. $50,001–$50,001) from the header's "$200?" cap-gains threshold
/// bleeding into the amount text. The parser must scrub the block wherever it lands and keep
/// the row's wrapped remainder.
/// </summary>
public class HouseDisclosureClientReprintedHeaderScrubTests
{
    private const string HeaderBlock =
        "ID Owner Asset Transaction Date Notification Amount Cap. Type Date Gains >";

    private static readonly DateOnly FilingDate = new(2026, 2, 1);

    private static List<DisclosureTransaction> Parse(params string[] lines) =>
        HouseDisclosureClient.ParseTransactionLines(lines, "Nancy Pelosi", FilingDate);

    [Fact]
    public void ParseTransactionLines_HeaderBlockMergedIntoAnchorLine_IsScrubbedFromAssetName()
    {
        // The production shape behind the 707 polluted rows: the reprinted header words sit
        // inside the row's own reconstructed line, splitting the wrapped asset name.
        var result = Parse(
            $"SP Tempus AI, Inc. - Class A Common {HeaderBlock} Stock (TEM) P 01/16/2026 01/16/2026 $50,001 - $100,000",
            "F      S     : New"
        );

        var tx = result.Should().ContainSingle().Subject;
        tx.AssetName.Should().Be("Tempus AI, Inc. - Class A Common Stock (TEM)");
        tx.Ticker.Should().Be("TEM");
        tx.OwnerType.Should().Be("SP");
        tx.AmountFrom.Should().Be(50_001);
        tx.AmountTo.Should().Be(100_000);
    }

    [Fact]
    public void ParseTransactionLines_StandaloneHeaderLine_WrappedRemainderStillFlowsIntoRow()
    {
        // Page break mid-row: the header reprint separates the row from its wrapped remainder
        // (rest of the asset name + the upper amount bound). The old guard stopped at the
        // header, truncating the asset name and misreading "$50,001 -" as "up to $50,001".
        var result = Parse(
            "SP Tempus AI, Inc. - Class A Common P 01/16/2026 01/16/2026 $50,001 -",
            HeaderBlock,
            "Stock (TEM) $100,000",
            "F      S     : New"
        );

        var tx = result.Should().ContainSingle().Subject;
        tx.AssetName.Should().Be("Tempus AI, Inc. - Class A Common Stock (TEM)");
        tx.Ticker.Should().Be("TEM");
        tx.AmountFrom.Should().Be(50_001);
        tx.AmountTo.Should().Be(100_000);
    }

    [Fact]
    public void ParseTransactionLines_HeaderLineWithCapGainsThreshold_ThresholdNeverBleedsIntoAmount()
    {
        // The header's "$200?" cap-gains threshold is a dollar token: unscrubbed, it becomes
        // the range's "upper bound" and the corrupt pair collapses to an impossible bracket.
        var result = Parse(
            "JT Acme Corporation - Common P 02/10/2026 02/11/2026 $15,001 -",
            $"{HeaderBlock} $200?",
            "Stock (ACME) $50,000",
            "F      S     : New"
        );

        var tx = result.Should().ContainSingle().Subject;
        tx.AssetName.Should().Be("Acme Corporation - Common Stock (ACME)");
        tx.AmountFrom.Should().Be(15_001);
        tx.AmountTo.Should().Be(50_000);
    }

    [Fact]
    public void ParseTransactionLines_RowLeadingFilingId_IsStrippedAndOwnerCodeStillDetected()
    {
        // Newer PTR layouts print a numeric filing ID at the start of each row; glued onto
        // the row text it defeated the ^-anchored owner regex and was stored as part of the
        // asset name ("2000080040 JT Williams-Sonoma ..." — 386 production rows).
        var result = Parse(
            "2000080040 JT Williams-Sonoma, Inc. Common Stock (WSM) P 05/27/2021 05/28/2021 $15,001 - $50,000",
            "F      S     : New"
        );

        var tx = result.Should().ContainSingle().Subject;
        tx.AssetName.Should().Be("Williams-Sonoma, Inc. Common Stock (WSM)");
        tx.Ticker.Should().Be("WSM");
        tx.OwnerType.Should().Be("JT");
        tx.AmountFrom.Should().Be(15_001);
        tx.AmountTo.Should().Be(50_000);
    }

    [Fact]
    public void ParseTransactionLines_HeaderBetweenTwoCompleteRows_NeitherRowIsPolluted()
    {
        // A page break BETWEEN rows: the header must not attach to either neighbour.
        var result = Parse(
            "SP Apple Inc. - Common Stock (AAPL) P 03/03/2026 03/05/2026 $1,001 - $15,000",
            HeaderBlock,
            "Microsoft Corporation (MSFT) S 03/04/2026 03/06/2026 $1,001 - $15,000",
            "F      S     : New"
        );

        result.Should().HaveCount(2);
        result[0].AssetName.Should().Be("Apple Inc. - Common Stock (AAPL)");
        result[1].AssetName.Should().Be("Microsoft Corporation (MSFT)");
        result[1].TransactionType.Should().Be(CongressTransactionType.Sale);
    }
}
