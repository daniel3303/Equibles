using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Web.Services;
using Equibles.Web.ViewModels.Stocks;

namespace Equibles.UnitTests.Web;

/// <summary>
/// The split-aware Group overload restates every share count onto today's post-split
/// basis from the row's own report date before aggregation and classification. A split
/// between the two quarters otherwise reads as a phantom Increased (forward) or Reduced
/// (reverse) row for every unchanged holder. Restatement is per exact listed series: a
/// split attributed to the primary class must never rescale a sibling listing's count.
/// </summary>
public class HoldingsPositionGrouperSplitRestatementTests
{
    private const string PrimaryTicker = "NVDA";
    private static readonly DateOnly PreviousQuarter = new(2024, 3, 31);
    private static readonly DateOnly CurrentQuarter = new(2024, 6, 30);

    // 10:1 forward split between the quarters, attributed to the primary series.
    private static List<StockSplit> ForwardSplitBetweenQuarters() =>
        [
            new StockSplit
            {
                EffectiveDate = new DateOnly(2024, 6, 10),
                Numerator = 10m,
                Denominator = 1m,
                PriceSeriesTicker = PrimaryTicker,
            },
        ];

    private static InstitutionalHolding MakeHolding(
        Guid holderId,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares,
        long value,
        string listedTicker = null
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            InstitutionalHolderId = holderId,
            InstitutionalHolder = holder,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(30),
            Shares = shares,
            Value = value,
            ListedTicker = listedTicker,
        };

    [Fact]
    public void UnchangedHolderAcrossAForwardSplit_ClassifiesAsUnchanged()
    {
        // 1,000 shares held through the 10:1 split: filed as 1,000 then 10,000. As filed
        // that buckets Increased with a phantom +9,000 delta.
        var holderId = Guid.NewGuid();
        var holder = new InstitutionalHolder { Id = holderId, Name = "Steady Fund" };
        var previous = MakeHolding(holderId, holder, PreviousQuarter, shares: 1_000, value: 90);
        var current = MakeHolding(holderId, holder, CurrentQuarter, shares: 10_000, value: 100);

        var grouped = HoldingsPositionGrouper.Group(
            [current],
            [previous],
            null,
            ForwardSplitBetweenQuarters(),
            PrimaryTicker
        );

        grouped[PositionChangeType.Increased].Should().BeEmpty();
        var change = grouped[PositionChangeType.Unchanged].Should().ContainSingle().Subject;
        change.CurrentShares.Should().Be(10_000);
        change.PreviousShares.Should().Be(10_000);
    }

    [Fact]
    public void UnchangedHolderAcrossAReverseSplit_ClassifiesAsUnchanged()
    {
        // 30,000 shares through a 1-for-30 reverse split: filed as 30,000 then 1,000 —
        // as filed that reads as a near-total sell.
        var holderId = Guid.NewGuid();
        var holder = new InstitutionalHolder { Id = holderId, Name = "Patient Fund" };
        var previous = MakeHolding(holderId, holder, PreviousQuarter, shares: 30_000, value: 90);
        var current = MakeHolding(holderId, holder, CurrentQuarter, shares: 1_000, value: 100);
        List<StockSplit> reverse =
        [
            new StockSplit
            {
                EffectiveDate = new DateOnly(2024, 6, 10),
                Numerator = 1m,
                Denominator = 30m,
                PriceSeriesTicker = PrimaryTicker,
            },
        ];

        var grouped = HoldingsPositionGrouper.Group(
            [current],
            [previous],
            null,
            reverse,
            PrimaryTicker
        );

        grouped[PositionChangeType.Reduced].Should().BeEmpty();
        var change = grouped[PositionChangeType.Unchanged].Should().ContainSingle().Subject;
        change.PreviousShares.Should().Be(1_000);
    }

    [Fact]
    public void RealTrimAcrossTheSplit_StaysReducedWithRestatedCounts()
    {
        // Sold half through the 10:1 split: 1,000 pre-split → 5,000 post-split shares.
        // As filed that reads +4,000 (Increased).
        var holderId = Guid.NewGuid();
        var holder = new InstitutionalHolder { Id = holderId, Name = "Trimming Fund" };
        var previous = MakeHolding(holderId, holder, PreviousQuarter, shares: 1_000, value: 90);
        var current = MakeHolding(holderId, holder, CurrentQuarter, shares: 5_000, value: 50);

        var grouped = HoldingsPositionGrouper.Group(
            [current],
            [previous],
            null,
            ForwardSplitBetweenQuarters(),
            PrimaryTicker
        );

        var change = grouped[PositionChangeType.Reduced].Should().ContainSingle().Subject;
        change.PreviousShares.Should().Be(10_000);
        change.CurrentShares.Should().Be(5_000);
    }

    [Fact]
    public void SiblingListingRow_IsNeverRescaledByThePrimarySeriesSplit()
    {
        // The holder owns 1,000 primary shares plus 700 of a sibling class in both
        // quarters. The 10:1 split is attributed to the PRIMARY series only, so the
        // sibling's 700 must survive as-filed on both sides — an unscoped factor over
        // the mixed sum would read 700 as 7,000.
        var holderId = Guid.NewGuid();
        var holder = new InstitutionalHolder { Id = holderId, Name = "Two-Class Fund" };
        List<InstitutionalHolding> previous =
        [
            MakeHolding(holderId, holder, PreviousQuarter, shares: 1_000, value: 90),
            MakeHolding(
                holderId,
                holder,
                PreviousQuarter,
                shares: 700,
                value: 60,
                listedTicker: "NVDA-B"
            ),
        ];
        List<InstitutionalHolding> current =
        [
            MakeHolding(holderId, holder, CurrentQuarter, shares: 10_000, value: 100),
            MakeHolding(
                holderId,
                holder,
                CurrentQuarter,
                shares: 700,
                value: 70,
                listedTicker: "NVDA-B"
            ),
        ];

        var grouped = HoldingsPositionGrouper.Group(
            current,
            previous,
            null,
            ForwardSplitBetweenQuarters(),
            PrimaryTicker
        );

        var change = grouped[PositionChangeType.Unchanged].Should().ContainSingle().Subject;
        change.CurrentShares.Should().Be(10_700);
        change.PreviousShares.Should().Be(10_700);
    }

    [Fact]
    public void LegacyNullAttributedSplit_RestatesThePrimarySeries()
    {
        // Older captured splits carry no PriceSeriesTicker; they belong to the primary
        // series (PriceSeriesSplitScope's legacy-null rule).
        var holderId = Guid.NewGuid();
        var holder = new InstitutionalHolder { Id = holderId, Name = "Legacy Fund" };
        var previous = MakeHolding(holderId, holder, PreviousQuarter, shares: 1_000, value: 90);
        var current = MakeHolding(holderId, holder, CurrentQuarter, shares: 10_000, value: 100);
        List<StockSplit> legacy =
        [
            new StockSplit
            {
                EffectiveDate = new DateOnly(2024, 6, 10),
                Numerator = 10m,
                Denominator = 1m,
                PriceSeriesTicker = null,
            },
        ];

        var grouped = HoldingsPositionGrouper.Group(
            [current],
            [previous],
            null,
            legacy,
            PrimaryTicker
        );

        grouped[PositionChangeType.Unchanged].Should().ContainSingle();
    }

    [Fact]
    public void ThreeArgOverload_KeepsAsFiledBehaviour()
    {
        var holderId = Guid.NewGuid();
        var holder = new InstitutionalHolder { Id = holderId, Name = "As-Filed Fund" };
        var previous = MakeHolding(holderId, holder, PreviousQuarter, shares: 1_000, value: 90);
        var current = MakeHolding(holderId, holder, CurrentQuarter, shares: 10_000, value: 100);

        var grouped = HoldingsPositionGrouper.Group([current], [previous], null);

        grouped[PositionChangeType.Increased].Should().ContainSingle();
    }
}
