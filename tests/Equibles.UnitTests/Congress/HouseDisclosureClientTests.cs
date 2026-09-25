using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Tests for <see cref="HouseDisclosureClient"/>. The PTR parser is exercised
/// two ways: at the reconstructed-line level via the internal
/// <see cref="HouseDisclosureClient.ParseTransactionLines"/>, and end-to-end
/// against checked-in real House Clerk PTR PDFs (public-domain federal
/// disclosures) via <see cref="HouseDisclosureClient.ParsePtrPdf(byte[], string, DateOnly)"/>,
/// which proves the page-geometry line reconstruction too. The download flow is
/// exercised via stub <see cref="HttpMessageHandler"/>s.
/// </summary>
public class HouseDisclosureClientTests
{
    private static readonly DateOnly FilingDate = new(2024, 7, 2);

    private static List<DisclosureTransaction> Parse(params string[] lines) =>
        HouseDisclosureClient.ParseTransactionLines(lines, "Nancy Pelosi", FilingDate);

    // ---- reconstructed-line parsing ----

    [Fact]
    public void ParseTransactionLines_SpouseStockWithTickerAndAmountOnWrapLine_ParsesEverything()
    {
        // Real reconstructed shape (House PTR, Pelosi 2024): the owner code and
        // the start of the asset share the row that also carries the type and
        // both dates; the rest of the asset name, the bracketed asset-type code,
        // and the upper amount bound wrap onto the next line — including the
        // ticker itself ("(NVDA)" lands on the wrap line here).
        var result = Parse(
            "SP NVIDIA Corporation - Common P 06/26/2024 06/26/2024 $1,000,001 -",
            "Stock (NVDA) [ST] $5,000,000",
            "F      S     : New",
            "D          : Purchased 10,000 shares."
        );

        var tx = result.Should().ContainSingle().Subject;
        tx.Ticker.Should().Be("NVDA");
        tx.OwnerType.Should().Be("SP");
        tx.AssetType.Should().Be("ST");
        tx.TransactionType.Should().Be(CongressTransactionType.Purchase);
        tx.TransactionDate.Should().Be(new DateOnly(2024, 6, 26));
        tx.AmountFrom.Should().Be(1_000_001);
        tx.AmountTo.Should().Be(5_000_000);
    }

    [Fact]
    public void ParseTransactionLines_MemberOwnedRowWithBlankOwnerColumn_IsNotDropped()
    {
        // The headline bug (issue #2160): member-owned holdings leave the Owner
        // column blank, so the row does not start with an owner code. The old
        // owner-code-anchored parser dropped every such row — which is the
        // majority of House trades — so no House member that traded only in
        // their own name ever landed in CongressMember. This row must parse with
        // a null owner.
        var result = Parse(
            "Apple Inc. - Common Stock (AAPL) P 03/03/2024 03/05/2024 $1,001 -",
            "$15,000",
            "F      S     : New"
        );

        var tx = result.Should().ContainSingle().Subject;
        tx.OwnerType.Should().BeNull("member-owned holdings leave the owner column blank");
        tx.Ticker.Should().Be("AAPL");
        tx.TransactionType.Should().Be(CongressTransactionType.Purchase);
        tx.AmountFrom.Should().Be(1_001);
        tx.AmountTo.Should().Be(15_000);
    }

    [Fact]
    public void ParseTransactionLines_PartialSaleWithWrappedAmount_ClassifiesAsSale()
    {
        var result = Parse(
            "SP Visa Inc. (V) [ST] S (partial) 07/01/2024 07/01/2024 $500,001 -",
            "$1,000,000",
            "F      S     : New"
        );

        var tx = result.Should().ContainSingle().Subject;
        tx.Ticker.Should().Be("V");
        tx.OwnerType.Should().Be("SP");
        tx.TransactionType.Should().Be(CongressTransactionType.Sale);
        tx.TransactionDate.Should().Be(new DateOnly(2024, 7, 1));
        tx.AmountFrom.Should().Be(500_001);
        tx.AmountTo.Should().Be(1_000_000);
    }

    [Fact]
    public void ParseTransactionLines_AssetTypeCodeButNoParenthesizedTicker_DoesNotMistakeCodeForTicker()
    {
        // A private-fund row (member-owned, blank owner) whose only bracketed
        // token is the asset-type code "[OI]". Without stripping the code first,
        // the ticker extractor would read "OI" — a real NYSE symbol — and
        // fabricate a trade. The row should still parse (so the member lands),
        // but with a null ticker so the downstream tracked-stock filter drops it.
        var result = Parse(
            "New Water Capital Partners II, LP P 10/12/2023 10/13/2023 $982",
            "(GLAS Funds, LP) [OI]",
            "D          : Capital Call for investment"
        );

        var tx = result.Should().ContainSingle().Subject;
        tx.Ticker.Should().BeNull("the bracketed asset-type code is not a ticker");
        tx.OwnerType.Should().BeNull();
        tx.TransactionType.Should().Be(CongressTransactionType.Purchase);
        tx.AssetType.Should().Be("OI");
        tx.AmountFrom.Should().Be(982);
        tx.AmountTo.Should().Be(982);
    }

    [Fact]
    public void ParseTransactionLines_OptionUnderNamedSubholding_RetainsFiledIdentity()
    {
        var result = Parse(
            "SP Acme Corporation Call Option (ACME) [OP] P 03/03/2025 03/05/2025 $15,001 - $50,000",
            "Filing Status: New",
            "Subholding Of: TIAA-CREF"
        );

        var tx = result.Should().ContainSingle().Subject;
        tx.AssetType.Should().Be("OP");
        tx.Subholding.Should().Be("TIAA-CREF");
        tx.AssetName.Should().NotContain("[OP]");
    }

    [Fact]
    public void ParseTransactionLines_InlineAbbreviatedSubholding_RetainsFiledIdentity()
    {
        var result = Parse(
            "Broadcom Inc. - Common Stock (AVGO) [OP] F S: New S O: 150 Main Street Trust > Pershing Advisor Solutions LLC Brokerage D: Put option, strike price P 03/03/2025 03/05/2025 $15,001 - $50,000"
        );

        var tx = result.Should().ContainSingle().Subject;
        tx.Ticker.Should().Be("AVGO");
        tx.AssetType.Should().Be("OP");
        tx.Subholding.Should()
            .Be("150 Main Street Trust > Pershing Advisor Solutions LLC Brokerage");
        tx.AssetName.Should().Be("Broadcom Inc. - Common Stock (AVGO)");
    }

    [Fact]
    public void ParseTransactionLines_CompactSubholdingOnMetadataLine_RetainsFiledIdentity()
    {
        var result = Parse(
            "Comfort Systems USA, Inc. Common Stock (FIX) [ST] P 01/13/2025 02/13/2025 $1,001 - $15,000",
            "F S: New S O: Joint Ownership LPL Account"
        );

        var tx = result.Should().ContainSingle().Subject;
        tx.Ticker.Should().Be("FIX");
        tx.AssetType.Should().Be("ST");
        tx.Subholding.Should().Be("Joint Ownership LPL Account");
        tx.AssetName.Should().Be("Comfort Systems USA, Inc. Common Stock (FIX)");
    }

    [Fact]
    public void ParseTransactionLines_NullPaddedCompactSubholding_RetainsFiledIdentity()
    {
        var result = Parse(
            "Procter & Gamble Company (PG) [ST] P 08/11/2026 08/20/2026 $1,001 - $15,000",
            "F\0\0\0\0\0 S\0\0\0\0\0: New S\0\0\0\0\0\0\0\0\0 O\0: David Taylor Trust > Sardinia Ready Mix 401(k) - Dave"
        );

        var tx = result.Should().ContainSingle().Subject;
        tx.Ticker.Should().Be("PG");
        tx.AssetType.Should().Be("ST");
        tx.Subholding.Should().Be("David Taylor Trust > Sardinia Ready Mix 401(k) - Dave");
        tx.AssetName.Should().Be("Procter & Gamble Company (PG)");
    }

    [Fact]
    public void ParseTransactionLines_InlineFilingStatusWithoutSubholding_StripsTheMetadataSuffix()
    {
        // The compact filing-status label can ride the asset line with NO subholding field.
        // Before the cut, the suffix was stored inside AssetName ("... F S: New"), creating a
        // polluted twin of the clean row a later replay produced (Chip Roy, 2026-08).
        var result = Parse(
            "Apple Inc. (AAPL) [ST] F S: New P 03/03/2025 03/05/2025 $1,001 - $15,000"
        );

        var tx = result.Should().ContainSingle().Subject;
        tx.Ticker.Should().Be("AAPL");
        tx.AssetType.Should().Be("ST");
        tx.Subholding.Should().BeNull();
        tx.AssetName.Should().Be("Apple Inc. (AAPL)");
    }

    [Fact]
    public void ParseTransactionLines_ExpandedFilingStatusWithoutSubholding_StripsTheMetadataSuffix()
    {
        var result = Parse(
            "Tesla Inc. (TSLA) [ST] Filing Status: Amended P 03/03/2025 03/05/2025 $1,001 - $15,000"
        );

        var tx = result.Should().ContainSingle().Subject;
        tx.Ticker.Should().Be("TSLA");
        tx.Subholding.Should().BeNull();
        tx.AssetName.Should().Be("Tesla Inc. (TSLA)");
    }

    [Fact]
    public void ParseTransactionLines_SeriesDAssetNameWithoutSubholding_PreservesAssetName()
    {
        var result = Parse(
            "Acme Series D: Preferred Stock (ACME) [ST] P 03/03/2025 03/05/2025 $15,001 - $50,000"
        );

        var tx = result.Should().ContainSingle().Subject;
        tx.Ticker.Should().Be("ACME");
        tx.Subholding.Should().BeNull();
        tx.AssetName.Should().Be("Acme Series D: Preferred Stock (ACME)");
    }

    [Fact]
    public void ParseTransactionLines_HeaderFieldLabelAndFooterLines_ProduceNoTransactions()
    {
        // None of these lines is a transaction row. The "Digitally Signed … ,
        // 02/23/2024" footer carries a date but no transaction-type anchor and
        // must not be misread as a trade.
        var result = Parse(
            "ID Owner Asset Transaction Date Notification Amount Cap.",
            "F      S     : New",
            "D          : Purchased 10,000 shares.",
            "* For the complete list of asset type abbreviations, please visit https://fd.house.gov/...",
            "Digitally Signed: Hon. Nancy Pelosi , 02/23/2024"
        );

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseTransactionLines_TransactionWithInvalidAmount_IsDropped()
    {
        var result = Parse(
            "Apple Inc. - Common Stock (AAPL) P 03/03/2024 03/05/2024 amount unavailable"
        );

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseTransactionLinesWithShape_UnanchoredRowLikeLine_IsRejected()
    {
        var result = HouseDisclosureClient.ParseTransactionLinesWithShape(
            [
                "Apple Inc. (AAPL) P 03/03/2024 03/05/2024 $1,001 - $15,000",
                "Brokerage: Microsoft Corp. (MSFT) Corrupt 04/01/2024 04/02/2024 $15,001 - $50,000",
            ],
            "Nancy Pelosi",
            FilingDate
        );

        result.Transactions.Should().ContainSingle();
        result.RejectedSourceRowCount.Should().Be(1);
    }

    [Fact]
    public void ParseTransactionLinesWithShape_WrappedMaturityDateWithOneDate_ContinuesTheRow()
    {
        // A bond's maturity wraps below its row ("Due" / "10/1/2033 [GS]"). A filed row prints
        // TWO dates on its anchor line, so a single date is a continuation, not a rejected row
        // — treating it as one dropped the asset-type code and shifted every later row index.
        var result = HouseDisclosureClient.ParseTransactionLinesWithShape(
            [
                "JT Illinois Housing Dev Auth 1.9%; Due P 04/14/2023 05/10/2023 $1,001 - $15,000",
                "10/1/2033 [GS]",
                "F      S     : New",
                "S          O : Morgan Stanley Trust Account",
                "JT NVIDIA Corporation (NVDA) [ST] P 04/14/2023 05/10/2023 $15,001 -",
                "$50,000",
            ],
            "Jonathan Jackson",
            FilingDate
        );

        result.RejectedSourceRowCount.Should().Be(0);
        result.Transactions.Should().HaveCount(2);
        var bond = result.Transactions[0];
        bond.SourceRowIndex.Should().Be(0);
        bond.AssetName.Should().Be("Illinois Housing Dev Auth 1.9%; Due 10/1/2033");
        bond.AssetType.Should().Be("GS");
        bond.Subholding.Should().Be("Morgan Stanley Trust Account");
        result.Transactions[1].SourceRowIndex.Should().Be(1);
        result.Transactions[1].AmountTo.Should().Be(50_000);
    }

    [Fact]
    public void ParseTransactionLinesWithShape_UnanchoredLineWithOneDate_IsRejected()
    {
        var result = HouseDisclosureClient.ParseTransactionLinesWithShape(
            [
                "Microsoft Corp. (MSFT) Corrupt 04/01/2024 $15,001 - $50,000",
                "Apple Inc. (AAPL) P 03/03/2024 03/05/2024 $1,001 - $15,000",
            ],
            "Nancy Pelosi",
            FilingDate
        );

        result.Transactions.Should().ContainSingle();
        result.Transactions[0].SourceRowIndex.Should().Be(1);
        result.RejectedSourceRowCount.Should().Be(1);
    }

    [Fact]
    public void ParseTransactionLinesWithShape_UnreadableRowAfterARow_IsRejectedNotGlued()
    {
        // A one-date line carrying an amount range is a row, not a wrapped fragment of the row
        // above: gluing it would lose the trade and name Apple "Apple Inc. Microsoft Corp.".
        var result = HouseDisclosureClient.ParseTransactionLinesWithShape(
            [
                "Apple Inc. (AAPL) P 03/03/2024 03/05/2024 $1,001 - $15,000",
                "Microsoft Corp. (MSFT) Corrupt 04/01/2024 $15,001 - $50,000",
            ],
            "Nancy Pelosi",
            FilingDate
        );

        result.RejectedSourceRowCount.Should().Be(1);
        var apple = result.Transactions.Should().ContainSingle().Subject;
        apple.SourceRowIndex.Should().Be(0);
        apple.Ticker.Should().Be("AAPL");
        apple.AssetName.Should().NotContain("Microsoft");
    }

    [Fact]
    public void ParseTransactionLinesWithShape_WrappedNotificationDate_IsRejectedNotGlued()
    {
        // A row whose notification date wrapped has one date and its range on the anchor line:
        // it is refused (and keeps its index) rather than read as part of another row.
        var result = HouseDisclosureClient.ParseTransactionLinesWithShape(
            [
                "Apple Inc. (AAPL) P 03/03/2024 $1,001 - $15,000",
                "03/05/2024",
                "Microsoft Corp. (MSFT) S 04/01/2024 04/03/2024 $15,001 - $50,000",
            ],
            "Nancy Pelosi",
            FilingDate
        );

        result.RejectedSourceRowCount.Should().Be(1);
        var msft = result.Transactions.Should().ContainSingle().Subject;
        msft.SourceRowIndex.Should().Be(1);
        msft.Ticker.Should().Be("MSFT");
        msft.AssetName.Should().NotContain("Apple");
    }

    [Fact]
    public void ParseTransactionLinesWithShape_WrappedBondMaturityAndDatedDate_ContinuesTheRow()
    {
        // Real lines (filing 20012179): a bond's maturity AND dated date wrap below its row.
        // Two dates with no amount range are asset text; counting them as a rejected row cut
        // the name short and pushed every later row onto the next SourceRowIndex.
        var result = HouseDisclosureClient.ParseTransactionLinesWithShape(
            [
                "E*Trade Financial Corp VAR RT s 07/15/2019 08/10/2019 $1,001 - $15,000",
                "gfedc",
                "09/15/2166 DTD 12/06/2017 [Cs]",
                "F IlINg s TATus : New",
                "D EsCRIPTIoN : Corporate bond",
                "HD supply Holdings, Inc. (HDs) P 07/25/2019 08/10/2019 $1,001 - $15,000",
                "gfedc",
                "[sT]",
            ],
            "Dean Phillips",
            FilingDate
        );

        result.RejectedSourceRowCount.Should().Be(0);
        result.Transactions.Should().HaveCount(2);
        var bond = result.Transactions[0];
        bond.AssetName.Should().Be("E*Trade Financial Corp VAR RT 09/15/2166 DTD 12/06/2017");
        bond.AssetType.Should().Be("CS");
        result.Transactions[1].SourceRowIndex.Should().Be(1);
        result.Transactions[1].Ticker.Should().Be("HDS");
    }

    [Theory]
    // An exact amount carries no range start; the lower-case "p" is not a marker we accept.
    [InlineData("sP Microsoft Corp. (msFT) [sT] p 04/01/2024 04/02/2024 $982.18")]
    // An unknown type letter with both dates and no amount at all.
    [InlineData("Microsoft Corp. (MSFT) [ST] X 04/01/2024 04/02/2024")]
    public void ParseTransactionLinesWithShape_UnanchoredLineWithAdjacentDates_IsRejectedNotGlued(
        string unreadableRow
    )
    {
        var result = HouseDisclosureClient.ParseTransactionLinesWithShape(
            ["Apple Inc. (AAPL) P 03/03/2024 03/05/2024 $1,001 - $15,000", unreadableRow],
            "Nancy Pelosi",
            FilingDate
        );

        result.RejectedSourceRowCount.Should().Be(1);
        var apple = result.Transactions.Should().ContainSingle().Subject;
        apple.AssetName.Should().Be("Apple Inc. (AAPL)");
        apple.AssetType.Should().BeNull();
    }

    [Fact]
    public void ParseTransactionLinesWithShape_UnreadableRowWithTopBracketAmount_IsRejected()
    {
        var result = HouseDisclosureClient.ParseTransactionLinesWithShape(
            [
                "Apple Inc. (AAPL) P 03/03/2024 03/05/2024 $1,001 - $15,000",
                "Microsoft Corp. (MSFT) Corrupt 04/01/2024 04/02/2024 Over $50,000,000",
            ],
            "Nancy Pelosi",
            FilingDate
        );

        result.RejectedSourceRowCount.Should().Be(1);
        result
            .Transactions.Should()
            .ContainSingle()
            .Which.AssetName.Should()
            .NotContain("Microsoft");
    }

    [Fact]
    public void ParseTransactionLinesWithShape_BondSeriesEBeforeTheMarker_IsAPurchaseNotAnExchange()
    {
        // "Ser E 03/15/2035" is part of the bond's name. The row's own marker is the LAST one
        // on the line and carries both dates; reading the series letter as an exchange would
        // skip the purchase by policy and record the filing without it.
        var result = HouseDisclosureClient.ParseTransactionLinesWithShape(
            [
                "JT NY ST Dorm Auth Rev 5% Ser E 03/15/2035 [GS] P 03/03/2024 03/05/2024 $1,001 - $15,000",
            ],
            "Nancy Pelosi",
            FilingDate
        );

        result.PolicySkippedRowCount.Should().Be(0);
        result.RejectedSourceRowCount.Should().Be(0);
        var bond = result.Transactions.Should().ContainSingle().Subject;
        bond.TransactionType.Should().Be(CongressTransactionType.Purchase);
        bond.TransactionDate.Should().Be(new DateOnly(2024, 3, 3));
        bond.AssetName.Should().Be("NY ST Dorm Auth Rev 5% Ser E 03/15/2035");
        bond.AssetType.Should().Be("GS");
    }

    [Fact]
    public void ParseTransactionLinesWithShape_WrappedBondSeriesELine_ContinuesTheRow()
    {
        var result = HouseDisclosureClient.ParseTransactionLinesWithShape(
            [
                "JT NY ST Dorm Auth Rev 5% Ser P 03/03/2024 03/05/2024 $1,001 - $15,000",
                "E 03/15/2035 [GS]",
                "JT NVIDIA Corporation (NVDA) [ST] S 04/14/2023 05/10/2023 $15,001 - $50,000",
            ],
            "Nancy Pelosi",
            FilingDate
        );

        result.PolicySkippedRowCount.Should().Be(0);
        result.RejectedSourceRowCount.Should().Be(0);
        result.Transactions.Should().HaveCount(2);
        var bond = result.Transactions[0];
        bond.TransactionType.Should().Be(CongressTransactionType.Purchase);
        bond.AssetName.Should().Be("NY ST Dorm Auth Rev 5% Ser E 03/15/2035");
        bond.AssetType.Should().Be("GS");
        result.Transactions[1].SourceRowIndex.Should().Be(1);
        result.Transactions[1].Ticker.Should().Be("NVDA");
    }

    [Fact]
    public void ParseTransactionLinesWithShape_SmallCapsFontRows_ParseInScrambledCase()
    {
        // Some official PDFs embed a small-caps font whose glyphs extract in scrambled case:
        // the marker ("s (partial)"), the owner ("sP"), the ticker ("(aaPl)") and the labels
        // ("F IlINg s TaTus :", "D EsCRIPTIoN :"). Every row of such a filing used to be
        // refused as malformed, and a label carrying a date used to count as a rejected row.
        var result = HouseDisclosureClient.ParseTransactionLinesWithShape(
            [
                "sP apple Inc. (aaPl) [sT] s (partial) 03/22/2019 04/03/2019 $1,001 - $15,000",
                "gfedcb",
                "F IlINg s TaTus : New",
                "D EsCRIPTIoN : redeemed partial call payout 5.75%, 11/01/2024",
                "sP Methanex Corporation (MEoH) P 05/09/2019 06/04/2019 $1,001 - $15,000",
                "gfedc",
                "[sT]",
                "F IlINg s TaTus : New",
                "S UbHOLDINg O F : Sara Jacobs Trust U/A DTD 09/01/2009 > Merrill Lynch",
            ],
            "Earl Blumenauer",
            FilingDate
        );

        result.RejectedSourceRowCount.Should().Be(0);
        result.Transactions.Should().HaveCount(2);

        var sale = result.Transactions[0];
        sale.SourceRowIndex.Should().Be(0);
        sale.TransactionType.Should().Be(CongressTransactionType.Sale);
        sale.OwnerType.Should().Be("SP");
        sale.Ticker.Should().Be("AAPL");
        sale.AssetType.Should().Be("ST");
        sale.AssetName.Should().Be("apple Inc. (aaPl)");
        sale.TransactionDate.Should().Be(new DateOnly(2019, 3, 22));

        var purchase = result.Transactions[1];
        purchase.SourceRowIndex.Should().Be(1);
        purchase.Ticker.Should().Be("MEOH");
        purchase.AssetType.Should().Be("ST");
        purchase.AssetName.Should().Be("Methanex Corporation (MEoH)");
        purchase.Subholding.Should().Be("Sara Jacobs Trust U/A DTD 09/01/2009 > Merrill Lynch");
    }

    [Fact]
    public void ParseTransactionLinesWithShape_ExchangeRow_IsSkippedByPolicyAndKeepsItsRowIndex()
    {
        // "E" is a filed exchange, a row with no trade type of ours (the Senate parser skips
        // "Exchange" the same way). It is not a parse failure, and it takes a row index so a
        // parser that later reads exchanges cannot shift the rows after it.
        var result = HouseDisclosureClient.ParseTransactionLinesWithShape(
            [
                "2000063452 SP Brookfield Infrastructure E 04/03/2020 04/11/2020 $15,001 -",
                "Partners LP Class A $50,000",
                "(BIPC) [ST]",
                "F ILINg S TATuS : Amended",
                "S uBHOLDINg O F : Neuberger Berman - Traditional IRA",
                "D ESCRIPTION : I exchanged Brookfield Partnership earlier this day for this stock.",
                "SP Tapestry, Inc. (TPR) [ST] P 05/15/2019 06/04/2019 $1,001 - $15,000",
            ],
            "Alan S. Lowenthal",
            FilingDate
        );

        result.RejectedSourceRowCount.Should().Be(0);
        result.PolicySkippedRowCount.Should().Be(1);
        var tx = result.Transactions.Should().ContainSingle().Subject;
        tx.Ticker.Should().Be("TPR");
        tx.SourceRowIndex.Should().Be(1);
    }

    [Fact]
    public void ParseTransactionLinesWithShape_ScrambledCaseReprintedHeader_IsScrubbedFromTheRow()
    {
        var result = HouseDisclosureClient.ParseTransactionLinesWithShape(
            [
                "SP Tapestry, Inc. (TPR) [ST] P 05/15/2019 06/04/2019 $1,001 - $15,000",
                "gfedc iD owner asset transaction Date notification amount cap. type Date gains > $200?",
                "F ILINg S TATuS : New",
            ],
            "Alan S. Lowenthal",
            FilingDate
        );

        var tx = result.Transactions.Should().ContainSingle().Subject;
        tx.AssetName.Should().Be("Tapestry, Inc. (TPR)");
        tx.AmountTo.Should().Be(15_000);
    }

    // ---- end-to-end against real PTR PDFs (page-geometry reconstruction) ----

    [Fact]
    public void ParsePtrPdf_RealSpouseStockFiling_ExtractsAllTradedTickers()
    {
        var bytes = File.ReadAllBytes(FixturePath("house-ptr-spouse-stocks.pdf"));

        var result = HouseDisclosureClient.ParsePtrPdf(bytes, "Nancy Pelosi", FilingDate);

        result.Select(t => t.Ticker).Should().BeEquivalentTo(["AVGO", "NVDA", "TSLA", "V"]);

        var nvda = result.Single(t => t.Ticker == "NVDA");
        nvda.OwnerType.Should().Be("SP");
        nvda.TransactionType.Should().Be(CongressTransactionType.Purchase);
        nvda.TransactionDate.Should().Be(new DateOnly(2024, 6, 26));
        nvda.AmountFrom.Should().Be(1_000_001);
        nvda.AmountTo.Should().Be(5_000_000);

        result
            .Single(t => t.Ticker == "TSLA")
            .TransactionType.Should()
            .Be(CongressTransactionType.Sale);
        result
            .Single(t => t.Ticker == "V")
            .TransactionType.Should()
            .Be(CongressTransactionType.Sale);
    }

    [Fact]
    public void ParsePtrPdf_RealMemberOwnedFiling_ParsesRowThatHasNoOwnerCode()
    {
        // Regression for #2160: this real PTR is a single member-owned row with
        // a blank owner column. The old parser produced zero transactions for
        // it (and every filing like it), so the member never landed. It must now
        // parse — with a null owner and (being a private fund) a null ticker.
        var bytes = File.ReadAllBytes(FixturePath("house-ptr-own-trade.pdf"));

        var result = HouseDisclosureClient.ParsePtrPdf(bytes, "Max Miller", FilingDate);

        var tx = result.Should().ContainSingle().Subject;
        tx.OwnerType.Should().BeNull();
        tx.Ticker.Should().BeNull();
        tx.TransactionType.Should().Be(CongressTransactionType.Purchase);
    }

    [Fact]
    public void ParsePtrPdf_RealSmallCapsFontFiling_ParsesEveryRow()
    {
        // Blumenauer 2019 (DocID 20011791): the small-caps font renders "s (partial)" markers
        // and "F IlINg s TaTus :" labels. Production refused this filing every cycle for years
        // because half its rows read as malformed, and sibling filings with only "P" rows were
        // stored with the scrambled label glued onto the asset name.
        var bytes = File.ReadAllBytes(FixturePath("house-ptr-small-caps-font.pdf"));

        var result = HouseDisclosureClient.ParsePtrPdfWithShape(
            bytes,
            "Earl Blumenauer",
            new DateOnly(2019, 6, 4)
        );

        result.HasExtractableText.Should().BeTrue();
        result.RejectedSourceRowCount.Should().Be(0);
        result.PolicySkippedRowCount.Should().Be(0);
        result.Transactions.Should().HaveCount(8);
        result
            .Transactions.Where(t => t.Ticker != null)
            .Select(t => t.Ticker)
            .Should()
            .BeEquivalentTo(["STAY", "MEOH", "TPR", "XRX"]);
        result.Transactions.Should().OnlyContain(t => t.OwnerType == "SP");
        result
            .Transactions.Count(t => t.TransactionType == CongressTransactionType.Sale)
            .Should()
            .Be(4);
        result
            .Transactions.Should()
            .OnlyContain(
                t => !t.AssetName.Contains("TaTus", StringComparison.OrdinalIgnoreCase),
                "the scrambled filing-status label is row metadata, never part of the name"
            );
        result.Transactions.Select(t => t.SourceRowIndex).Should().Equal(0, 1, 2, 3, 4, 5, 6, 7);
    }

    [Fact]
    public void ParsePtrPdf_RealExchangeOnlyFiling_IsSkippedByPolicyNotRejected()
    {
        // Lowenthal 2020 (DocID 20016428): two "E" rows and nothing else.
        var bytes = File.ReadAllBytes(FixturePath("house-ptr-exchange-rows.pdf"));

        var result = HouseDisclosureClient.ParsePtrPdfWithShape(
            bytes,
            "Alan S. Lowenthal",
            new DateOnly(2020, 4, 18)
        );

        result.HasExtractableText.Should().BeTrue();
        result.Transactions.Should().BeEmpty();
        result.RejectedSourceRowCount.Should().Be(0);
        result.PolicySkippedRowCount.Should().Be(2);
    }

    [Fact]
    public void ParsePtrPdf_RealWrappedMaturityDateFiling_KeepsTheBondRowWhole()
    {
        // Jackson 2023 (DocID 20022795): ten rows, one a municipal bond whose "Due 10/1/2033
        // [GS]" wraps onto its own line. That fragment used to be counted as a rejected row,
        // which refused the whole filing.
        var bytes = File.ReadAllBytes(FixturePath("house-ptr-wrapped-maturity-date.pdf"));

        var result = HouseDisclosureClient.ParsePtrPdfWithShape(
            bytes,
            "Jonathan Jackson",
            new DateOnly(2023, 5, 12)
        );

        result.RejectedSourceRowCount.Should().Be(0);
        result.Transactions.Should().HaveCount(10);
        var bond = result.Transactions.Single(t => t.AssetName.Contains("Illinois Housing"));
        bond.AssetName.Should().EndWith("Due 10/1/2033");
        bond.AssetType.Should().Be("GS");
        bond.Subholding.Should().Be("Morgan Stanley Trust Account");
        result
            .Transactions.Select(t => t.Ticker)
            .Should()
            .BeEquivalentTo([null, "AME", "BHF", "BHF", "DE", null, "NVDA", "PH", "UNH", "V"]);
    }

    [Fact]
    public void ParsePtrPdf_RealScannedPaperFiling_HasNoExtractableText()
    {
        // A paper PTR scanned by the Clerk (seven-digit DocID) opens as a valid PDF with no
        // text layer at all — the one deterministic signal that separates it from an
        // electronic filing the parser failed on.
        var bytes = File.ReadAllBytes(FixturePath("house-ptr-scanned-paper.pdf"));

        var result = HouseDisclosureClient.ParsePtrPdfWithShape(
            bytes,
            "Scanned Member",
            new DateOnly(2026, 1, 1)
        );

        result.HasExtractableText.Should().BeFalse();
        result.Transactions.Should().BeEmpty();
        result.RejectedSourceRowCount.Should().Be(0);
    }

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "Congress", fileName);

    // ---- download flow ----

    [Fact]
    public async Task GetRecentTransactions_OfficialMixedFilingTypes_DoNotInvalidatePtrIndex()
    {
        const int year = 2025;
        var xml = await File.ReadAllTextAsync(FixturePath("house-fd-2025-mixed-types.xml"));
        var zipBytes = BuildZipWithSingleEntry($"{year}FD.xml", xml);
        var handler = new UrlRoutingHandler(zipBytes);
        using var httpClient = new HttpClient(handler);
        var sut = new HouseDisclosureClient(
            httpClient,
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        var result = await sut.GetRecentTransactions(
            new DateOnly(year, 1, 1),
            new DateOnly(year, 12, 31),
            new HashSet<string> { "20032062" },
            CancellationToken.None
        );

        result.IsComplete.Should().BeTrue();
        result.Transactions.Should().BeEmpty();
        handler.Requests.Should().ContainSingle("the known PTR is already checkpointed");
    }

    [Theory]
    [InlineData("F")]
    [InlineData("N")]
    [InlineData("R")]
    public async Task GetRecentTransactions_HistoricalOfficialNonPtrType_IsComplete(
        string filingType
    )
    {
        const int year = 2012;
        var xml = $"""
            <FinancialDisclosure>
              <Member>
                <First>Jane</First><Last>Doe</Last><FilingType>{filingType}</FilingType>
                <StateDst>CA01</StateDst><Year>{year}</Year>
                <FilingDate>4/30/{year}</FilingDate><DocID>8200000</DocID>
              </Member>
            </FinancialDisclosure>
            """;
        var zipBytes = BuildZipWithSingleEntry($"{year}FD.xml", xml);
        var handler = new UrlRoutingHandler(zipBytes);
        using var httpClient = new HttpClient(handler);
        var sut = new HouseDisclosureClient(
            httpClient,
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        var result = await sut.GetRecentTransactions(
            new DateOnly(year, 1, 1),
            new DateOnly(year, 12, 31),
            new HashSet<string>(),
            CancellationToken.None
        );

        result.IsComplete.Should().BeTrue();
        result.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentTransactions_FdZipReturns404ForYear_ReturnsEmptyListWithoutThrowing()
    {
        // A missing year index cannot prove an archive partition is complete. The client
        // contains the failure, marks the result incomplete, and avoids PTR requests.
        var handler = new ConstantStatusHandler(HttpStatusCode.NotFound);
        using var httpClient = new HttpClient(handler);
        var sut = new HouseDisclosureClient(
            httpClient,
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        var result = await sut.GetRecentTransactions(
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            new HashSet<string>(),
            CancellationToken.None
        );

        result.Transactions.Should().BeEmpty();
        result.IsComplete.Should().BeFalse();
        handler
            .Requests.Should()
            .ContainSingle(
                "only the year's FD ZIP should be requested — a 404 must not cascade into per-filing PDF downloads"
            );
    }

    [Fact]
    public async Task GetRecentTransactions_FdZipMissingExpectedXmlEntry_ReturnsEmptyListAndDoesNotRequestAnyPtrPdf()
    {
        // A 200 ZIP whose {year}FD.xml entry is missing/misnamed is incomplete, not an
        // authoritative empty year, and must not cascade into PTR PDF downloads.
        var zipBytes = BuildZipWithSingleEntry("wrongname.xml", "<irrelevant />");
        var handler = new BytesContentHandler(zipBytes, "application/zip");
        using var httpClient = new HttpClient(handler);
        var sut = new HouseDisclosureClient(
            httpClient,
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        var result = await sut.GetRecentTransactions(
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            new HashSet<string>(),
            CancellationToken.None
        );

        result.Transactions.Should().BeEmpty();
        result.IsComplete.Should().BeFalse();
        handler
            .Requests.Should()
            .ContainSingle(
                "the missing-XML-entry branch must not cascade into per-filing PTR PDF downloads"
            );
        handler.Requests[0].Should().Contain("2025FD.zip");
    }

    [Fact]
    public async Task GetRecentTransactions_IndexXmlHasWrongShape_MarksYearIncomplete()
    {
        const int year = 2025;
        var zipBytes = BuildZipWithSingleEntry($"{year}FD.xml", "<html />");
        var handler = new UrlRoutingHandler(zipBytes);
        using var httpClient = new HttpClient(handler);
        var sut = new HouseDisclosureClient(
            httpClient,
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        var result = await sut.GetRecentTransactions(
            new DateOnly(year, 1, 1),
            new DateOnly(year, 12, 31),
            new HashSet<string>(),
            CancellationToken.None
        );

        result.IsComplete.Should().BeFalse();
        result.Transactions.Should().BeEmpty();
        result.ProcessedFilings.Should().BeEmpty();
        handler.Requests.Should().ContainSingle("the invalid index must not request a PDF");
    }

    [Fact]
    public async Task GetRecentTransactions_IndexMemberWithoutFilingType_MarksYearIncomplete()
    {
        const int year = 2025;
        var xml = """
            <FinancialDisclosures>
              <Member><First>Jane</First><Last>Doe</Last><DocID>20251234</DocID><FilingDate>02/01/2025</FilingDate></Member>
            </FinancialDisclosures>
            """;
        var zipBytes = BuildZipWithSingleEntry($"{year}FD.xml", xml);
        var handler = new UrlRoutingHandler(zipBytes);
        using var httpClient = new HttpClient(handler);
        var sut = new HouseDisclosureClient(
            httpClient,
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        var result = await sut.GetRecentTransactions(
            new DateOnly(year, 1, 1),
            new DateOnly(year, 12, 31),
            new HashSet<string>(),
            CancellationToken.None
        );

        result.IsComplete.Should().BeFalse();
        result.ProcessedFilings.Should().BeEmpty();
        handler.Requests.Should().ContainSingle("the malformed index row must not request a PDF");
    }

    [Fact]
    public async Task GetRecentTransactions_MalformedPtrIndexRow_MarksYearIncomplete()
    {
        const int year = 2025;
        var xml = $"""
            <FinancialDisclosures>
              <Member>
                <First>Jane</First>
                <Last>Doe</Last>
                <FilingType>P</FilingType>
                <StateDst>CA01</StateDst>
                <FilingDate>not-a-date</FilingDate>
                <DocID>20251234</DocID>
              </Member>
            </FinancialDisclosures>
            """;
        var zipBytes = BuildZipWithSingleEntry($"{year}FD.xml", xml);
        var handler = new UrlRoutingHandler(zipBytes);
        using var httpClient = new HttpClient(handler);
        var sut = new HouseDisclosureClient(
            httpClient,
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        var result = await sut.GetRecentTransactions(
            new DateOnly(year, 1, 1),
            new DateOnly(year, 12, 31),
            new HashSet<string>(),
            CancellationToken.None
        );

        result.IsComplete.Should().BeFalse();
        result.ProcessedFilings.Should().BeEmpty();
        handler.Requests.Should().ContainSingle("the malformed index row must not request a PDF");
    }

    [Fact]
    public async Task GetRecentTransactions_MalformedPtrPdfBytes_SkipsFilingWithoutThrowing()
    {
        // The per-filing catch in GetRecentTransactions scopes a bad PDF
        // (truncated CDN response, corrupt upload) to a single skipped filing
        // instead of letting PdfDocument.Open's exception abort the remaining
        // year of filings.
        const int year = 2025;
        const string docId = "20251234";
        var xml = $"""
            <FinancialDisclosures>
              <Member>
                <Prefix>Hon.</Prefix>
                <First>Jane</First>
                <Last>Doe</Last>
                <FilingType>P</FilingType>
                <StateDst>CA01</StateDst>
                <Year>{year}</Year>
                <FilingDate>2/1/{year}</FilingDate>
                <DocID>{docId}</DocID>
              </Member>
            </FinancialDisclosures>
            """;
        var zipBytes = BuildZipWithSingleEntry($"{year}FD.xml", xml);
        var handler = new UrlRoutingHandler(zipBytes, pdfBytes: [0x00, 0x01, 0x02, 0x03, 0x04]);
        using var httpClient = new HttpClient(handler);
        var sut = new HouseDisclosureClient(
            httpClient,
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        var result = await sut.GetRecentTransactions(
            new DateOnly(year, 1, 1),
            new DateOnly(year, 12, 31),
            new HashSet<string>(),
            CancellationToken.None
        );

        result.Transactions.Should().BeEmpty();
        result.IsComplete.Should().BeFalse();
        result
            .ProcessedFilings.Should()
            .BeEmpty("an unreadable download is retried, never recorded");
        handler
            .Requests.Should()
            .Contain(
                r => r.Contains($"ptr-pdfs/{year}/{docId}.pdf"),
                "the corrupt PDF must have been fetched before its parse failure was scoped"
            );
    }

    [Theory]
    [InlineData("house-ptr-scanned-paper.pdf")]
    [InlineData("house-ptr-exchange-rows.pdf")]
    public async Task GetRecentTransactions_DeterministicNoRowVerdict_IsRecordedAsProcessed(
        string fixture
    )
    {
        // A scanned paper report and an exchange-only report both yield no trades, and both
        // verdicts are deterministic — re-reading the same bytes cannot change them — so the
        // filing is recorded with zero items and the cycle stays complete. Before this the
        // per-filing catch left them unrecorded and production re-downloaded 1,330 such filings
        // every cycle, and the House 2019 archive partition could never complete.
        const int year = 2025;
        const string docId = "20251234";
        var xml = $"""
            <FinancialDisclosures>
              <Member>
                <Prefix>Hon.</Prefix>
                <First>Jane</First>
                <Last>Doe</Last>
                <FilingType>P</FilingType>
                <StateDst>CA01</StateDst>
                <Year>{year}</Year>
                <FilingDate>2/1/{year}</FilingDate>
                <DocID>{docId}</DocID>
              </Member>
            </FinancialDisclosures>
            """;
        var zipBytes = BuildZipWithSingleEntry($"{year}FD.xml", xml);
        var handler = new UrlRoutingHandler(
            zipBytes,
            pdfBytes: await File.ReadAllBytesAsync(FixturePath(fixture))
        );
        using var httpClient = new HttpClient(handler);
        var sut = new HouseDisclosureClient(
            httpClient,
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        var result = await sut.GetRecentTransactions(
            new DateOnly(year, 1, 1),
            new DateOnly(year, 12, 31),
            new HashSet<string>(),
            CancellationToken.None
        );

        result.Transactions.Should().BeEmpty();
        result.IsComplete.Should().BeTrue();
        result
            .ProcessedFilings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new ProcessedFiling(docId, new DateOnly(year, 2, 1), 0));
    }

    [Fact]
    public async Task GetRecentTransactions_FilingWithARejectedRow_RecordsTheRowsItCouldRead()
    {
        // A row the parser cannot read is a deterministic loss: the recognized rows are stored,
        // the filing is recorded, and only a parser version bump re-reads it. Refusing the whole
        // filing (the v6 rule) discarded its good rows every cycle and never recovered the bad one.
        const int year = 2025;
        const string docId = "20255678";
        var xml = $"""
            <FinancialDisclosures>
              <Member>
                <Prefix>Hon.</Prefix>
                <First>Jane</First>
                <Last>Doe</Last>
                <FilingType>P</FilingType>
                <StateDst>CA01</StateDst>
                <Year>{year}</Year>
                <FilingDate>2/1/{year}</FilingDate>
                <DocID>{docId}</DocID>
              </Member>
            </FinancialDisclosures>
            """;
        var zipBytes = BuildZipWithSingleEntry($"{year}FD.xml", xml);
        var handler = new UrlRoutingHandler(
            zipBytes,
            pdfBytes: BuildTextPdf(
                "Apple Inc. (AAPL) P 03/03/2024 03/05/2024 $1,001 - $15,000",
                "Brokerage: Microsoft Corp. (MSFT) Corrupt 04/01/2024 04/02/2024 $15,001 - $50,000"
            )
        );
        using var httpClient = new HttpClient(handler);
        var sut = new HouseDisclosureClient(
            httpClient,
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        var result = await sut.GetRecentTransactions(
            new DateOnly(year, 1, 1),
            new DateOnly(year, 12, 31),
            new HashSet<string>(),
            CancellationToken.None
        );

        result.IsComplete.Should().BeTrue();
        result.Transactions.Should().ContainSingle().Which.Ticker.Should().Be("AAPL");
        result
            .ProcessedFilings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new ProcessedFiling(docId, new DateOnly(year, 2, 1), 1, RejectedRowCount: 1));
    }

    // A one-page PDF with the given lines of Helvetica text, one per row, so the geometry
    // line reconstruction sees exactly those lines.
    private static byte[] BuildTextPdf(params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(612, 792);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var y = 700d;
        foreach (var line in lines)
        {
            page.AddText(line, 9, new PdfPoint(40, y), font);
            y -= 14;
        }
        return builder.Build();
    }

    [Fact]
    public async Task GetRecentTransactions_FdZipContainsOneMatchingFilingButPtrPdfIs404_EntersPerFilingForeachAndExitsCleanly()
    {
        const int year = 2025;
        const string docId = "20251234";
        var xml = $"""
            <FinancialDisclosures>
              <Member>
                <Prefix>Hon.</Prefix>
                <First>Jane</First>
                <Last>Doe</Last>
                <FilingType>P</FilingType>
                <StateDst>CA01</StateDst>
                <Year>{year}</Year>
                <FilingDate>2/1/{year}</FilingDate>
                <DocID>{docId}</DocID>
              </Member>
            </FinancialDisclosures>
            """;
        var zipBytes = BuildZipWithSingleEntry($"{year}FD.xml", xml);
        var handler = new UrlRoutingHandler(zipBytes);
        using var httpClient = new HttpClient(handler);
        var sut = new HouseDisclosureClient(
            httpClient,
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        var result = await sut.GetRecentTransactions(
            new DateOnly(year, 1, 1),
            new DateOnly(year, 12, 31),
            new HashSet<string>(),
            CancellationToken.None
        );

        result.Transactions.Should().BeEmpty();
        result.IsComplete.Should().BeFalse();
        handler
            .Requests.Should()
            .HaveCount(
                2,
                "one for the FD ZIP (with one matching filing inside) and one for the per-filing PtR PDF"
            );
        handler.Requests[0].Should().Contain($"{year}FD.zip");
        handler.Requests[1].Should().Contain($"ptr-pdfs/{year}/{docId}.pdf");
    }

    private static byte[] BuildZipWithSingleEntry(string entryName, string entryContent)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(entryContent);
        }
        return memory.ToArray();
    }

    private sealed class ConstantStatusHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        public List<string> Requests { get; } = new();

        public ConstantStatusHandler(HttpStatusCode statusCode) => _statusCode = statusCode;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }

    private sealed class BytesContentHandler : HttpMessageHandler
    {
        private readonly byte[] _content;
        private readonly string _contentType;
        public List<string> Requests { get; } = new();

        public BytesContentHandler(byte[] content, string contentType)
        {
            _content = content;
            _contentType = contentType;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request.RequestUri!.ToString());
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_content),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(_contentType);
            return Task.FromResult(response);
        }
    }

    private sealed class UrlRoutingHandler : HttpMessageHandler
    {
        private readonly byte[] _zipBytes;
        private readonly byte[] _pdfBytes;
        public List<string> Requests { get; } = new();

        public UrlRoutingHandler(byte[] zipBytes, byte[] pdfBytes = null)
        {
            _zipBytes = zipBytes;
            _pdfBytes = pdfBytes;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);

            if (url.Contains("/financial-pdfs/") && url.EndsWith("FD.zip"))
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_zipBytes),
                };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                return Task.FromResult(response);
            }

            if (url.Contains("/ptr-pdfs/"))
            {
                if (_pdfBytes == null)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

                var pdfResponse = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_pdfBytes),
                };
                pdfResponse.Content.Headers.ContentType = new MediaTypeHeaderValue(
                    "application/pdf"
                );
                return Task.FromResult(pdfResponse);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
