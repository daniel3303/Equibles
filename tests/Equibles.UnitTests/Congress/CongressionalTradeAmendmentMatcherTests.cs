using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class CongressionalTradeAmendmentMatcherTests
{
    private static readonly Guid MemberId = Guid.NewGuid();

    private static CongressionalTrade Trade(
        string sourceId,
        int row,
        DateOnly transactionDate,
        string ticker = "BRKB",
        string subholding = "Vanguard Joint Retirement",
        long amountFrom = 1_001,
        long amountTo = 15_000,
        DateOnly? filingDate = null
    ) =>
        new()
        {
            CongressMemberId = MemberId,
            FilingKind = CongressionalFilingKind.HousePeriodicTransactionReport,
            SourceId = sourceId,
            SourceRowIndex = row,
            FilingDate = filingDate ?? new DateOnly(2021, 7, 27),
            TransactionDate = transactionDate,
            TransactionType = CongressTransactionType.Purchase,
            OwnerType = "JT",
            AssetName = $"Asset ({ticker})",
            AssetType = "ST",
            FiledTicker = ticker,
            Subholding = subholding,
            AmountFrom = amountFrom,
            AmountTo = amountTo,
        };

    private static CongressionalTradeAmendment Row(
        CongressionalTrade filed,
        CongressionalTradeFilingStatus status
    ) => CongressionalTradeAmendmentMatcher.Record(filed, status);

    [Fact]
    public void Match_CastorYearTypos_PairsTheWholeGroupAgainstTheOneFiling()
    {
        // Each amended row differs from every original only by its date, so no row pairs alone;
        // the group of three against three is what makes the outcome unique.
        var originals = new[]
        {
            Trade("20019168", 0, new DateOnly(2020, 6, 18)),
            Trade("20019168", 1, new DateOnly(2020, 6, 25)),
            Trade("20019168", 2, new DateOnly(2020, 7, 16)),
        };
        var amendment = new[]
        {
            Row(
                Trade("20019181", 0, new DateOnly(2021, 7, 16)),
                CongressionalTradeFilingStatus.Amended
            ),
            Row(
                Trade("20019181", 1, new DateOnly(2021, 6, 25)),
                CongressionalTradeFilingStatus.Amended
            ),
            Row(
                Trade("20019181", 2, new DateOnly(2021, 6, 18)),
                CongressionalTradeFilingStatus.Amended
            ),
        };
        var earlierPurchases = new[]
        {
            Trade(
                "20018921",
                0,
                new DateOnly(2021, 5, 21),
                subholding: "E*Trade Brokerage Account"
            ),
        };

        var match = CongressionalTradeAmendmentMatcher.Match(
            amendment,
            [.. originals, .. earlierPurchases]
        );

        match.Verdict.Should().Be(CongressionalTradeAmendmentVerdict.Matched);
        match.Pairs.Select(pair => pair.Superseded).Should().BeEquivalentTo(originals);
        match.Pairs.Select(pair => pair.Amendment).Should().BeEquivalentTo(amendment);
    }

    [Fact]
    public void Match_HernAmendedAndDeletedRows_PairsEachWithItsOriginal()
    {
        var organon = Trade(
            "20035134",
            11,
            new DateOnly(2026, 8, 5),
            "OGN",
            "Hern Family Foundation"
        );
        var versant = Trade(
            "20035134",
            13,
            new DateOnly(2026, 8, 5),
            "VSNT",
            "Hern Family Foundation"
        );
        var comcast = Trade(
            "20035134",
            1,
            new DateOnly(2026, 8, 5),
            "CMCSA",
            "Hern Family Foundation"
        );
        var amended = Row(
            Trade("20035196", 0, new DateOnly(2026, 8, 5), "OGN", "Hern Family Revocable Trust"),
            CongressionalTradeFilingStatus.Amended
        );
        var deleted = Row(
            Trade("20035196", 1, new DateOnly(2026, 8, 5), "VSNT", "Hern Family Foundation"),
            CongressionalTradeFilingStatus.Deleted
        );

        var match = CongressionalTradeAmendmentMatcher.Match(
            [amended, deleted],
            [organon, versant, comcast]
        );

        match.Verdict.Should().Be(CongressionalTradeAmendmentVerdict.Matched);
        match.Pairs.Should().HaveCount(2);
        match.Pairs.Single(pair => pair.Amendment == amended).Superseded.Should().Be(organon);
        match.Pairs.Single(pair => pair.Amendment == deleted).Superseded.Should().Be(versant);
    }

    [Fact]
    public void Match_DeletedRowWithAChangedField_DoesNotPair()
    {
        var original = Trade(
            "A",
            0,
            new DateOnly(2026, 8, 5),
            amountFrom: 15_001,
            amountTo: 50_000
        );
        var deleted = Row(
            Trade("B", 0, new DateOnly(2026, 8, 5)),
            CongressionalTradeFilingStatus.Deleted
        );

        CongressionalTradeAmendmentMatcher
            .Match([deleted], [original])
            .Verdict.Should()
            .Be(CongressionalTradeAmendmentVerdict.NoPredecessor);
    }

    [Fact]
    public void Match_TwoFieldsChanged_HasNoPredecessor()
    {
        var original = Trade("A", 0, new DateOnly(2026, 8, 5));
        var amended = Row(
            Trade("B", 0, new DateOnly(2026, 8, 6), amountFrom: 15_001, amountTo: 50_000),
            CongressionalTradeFilingStatus.Amended
        );

        CongressionalTradeAmendmentMatcher
            .Match([amended], [original])
            .Verdict.Should()
            .Be(CongressionalTradeAmendmentVerdict.NoPredecessor);
    }

    [Fact]
    public void Match_ADifferentTicker_IsNeverACandidate()
    {
        var original = Trade("A", 0, new DateOnly(2026, 8, 5), ticker: "AAPL");
        var amended = Row(
            Trade("B", 0, new DateOnly(2026, 8, 5), ticker: "MSFT"),
            CongressionalTradeFilingStatus.Amended
        );

        CongressionalTradeAmendmentMatcher
            .Match([amended], [original])
            .Verdict.Should()
            .Be(CongressionalTradeAmendmentVerdict.NoPredecessor);
    }

    [Fact]
    public void Match_TwoFilingsExplainTheAmendment_IsAmbiguous()
    {
        // A habitual purchase: the corrected date could belong to either month's report.
        var january = Trade(
            "A",
            0,
            new DateOnly(2026, 1, 5),
            filingDate: new DateOnly(2026, 1, 20)
        );
        var february = Trade(
            "B",
            0,
            new DateOnly(2026, 2, 5),
            filingDate: new DateOnly(2026, 2, 20)
        );
        var amended = Row(
            Trade("C", 0, new DateOnly(2026, 1, 6), filingDate: new DateOnly(2026, 3, 1)),
            CongressionalTradeFilingStatus.Amended
        );

        CongressionalTradeAmendmentMatcher
            .Match([amended], [january, february])
            .Verdict.Should()
            .Be(CongressionalTradeAmendmentVerdict.Ambiguous);
    }

    [Fact]
    public void Match_MoreCandidatesThanRowsInOneFiling_IsAmbiguous()
    {
        var first = Trade("A", 0, new DateOnly(2026, 1, 5));
        var second = Trade("A", 1, new DateOnly(2026, 1, 12));
        var amended = Row(
            Trade("B", 0, new DateOnly(2026, 1, 6)),
            CongressionalTradeFilingStatus.Amended
        );

        CongressionalTradeAmendmentMatcher
            .Match([amended], [first, second])
            .Verdict.Should()
            .Be(CongressionalTradeAmendmentVerdict.Ambiguous);
    }

    [Fact]
    public void Match_ExactCopyPreferredOverOneFieldNeighbour_PairsTheCopy()
    {
        var copy = Trade("A", 0, new DateOnly(2026, 1, 5));
        var neighbour = Trade("A", 1, new DateOnly(2026, 1, 12));
        var amended = Row(
            Trade("B", 0, new DateOnly(2026, 1, 5)),
            CongressionalTradeFilingStatus.Amended
        );

        var match = CongressionalTradeAmendmentMatcher.Match([amended], [copy, neighbour]);

        match.Verdict.Should().Be(CongressionalTradeAmendmentVerdict.Matched);
        match.Pairs.Should().ContainSingle().Which.Superseded.Should().Be(copy);
    }

    [Fact]
    public void Match_ExactCopyInOneFiling_OutranksAOneFieldMatchInAnother()
    {
        // Moulton's 2023 amendment reprints the 2022 sale exactly; a later report holds the same
        // sale on another date, one changed field away.
        var reprinted = Trade("20022451", 0, new DateOnly(2022, 9, 16), "ATVI");
        var laterSale = Trade("20022611", 0, new DateOnly(2023, 3, 14), "ATVI");
        var amendment = Row(
            Trade("20022724", 0, new DateOnly(2022, 9, 16), "ATVI"),
            CongressionalTradeFilingStatus.Amended
        );

        var match = CongressionalTradeAmendmentMatcher.Match([amendment], [reprinted, laterSale]);

        match.Verdict.Should().Be(CongressionalTradeAmendmentVerdict.Matched);
        match.Pairs.Should().ContainSingle().Which.Superseded.Should().BeSameAs(reprinted);
    }

    [Fact]
    public void Match_AmendmentOfOneOfTwoIdenticalRows_SupersedesTheFirstTwin()
    {
        // Pelosi's 2019 report lists the same Visa sale twice; either twin is the one reprinted.
        var first = Trade("20012288", 1, new DateOnly(2019, 8, 7), "V");
        var second = Trade("20012288", 2, new DateOnly(2019, 8, 7), "V");
        var amendment = Row(
            Trade("20012343", 0, new DateOnly(2019, 8, 7), "V"),
            CongressionalTradeFilingStatus.Amended
        );

        var match = CongressionalTradeAmendmentMatcher.Match([amendment], [second, first]);

        match.Verdict.Should().Be(CongressionalTradeAmendmentVerdict.Matched);
        match.Pairs.Should().ContainSingle().Which.Superseded.Should().BeSameAs(first);
    }

    [Fact]
    public void Match_TwoFilingsHoldExactCopies_StaysAmbiguous()
    {
        // Blumenauer's deletion reprints a row two reports both carry; picking one would guess.
        var amendment = Row(
            Trade("20019618", 4, new DateOnly(2021, 9, 24), "LW"),
            CongressionalTradeFilingStatus.Deleted
        );

        var match = CongressionalTradeAmendmentMatcher.Match(
            [amendment],
            [
                Trade("20019580", 4, new DateOnly(2021, 9, 24), "LW"),
                Trade("20019619", 4, new DateOnly(2021, 9, 24), "LW"),
            ]
        );

        match.Verdict.Should().Be(CongressionalTradeAmendmentVerdict.Ambiguous);
        match.Pairs.Should().BeEmpty();
    }
}
