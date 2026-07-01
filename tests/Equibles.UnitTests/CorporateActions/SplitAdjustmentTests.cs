using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;

namespace Equibles.UnitTests.CorporateActions;

/// <summary>
/// <see cref="SplitAdjustment"/> converts a value observed as-of a past date
/// onto today's post-split basis. The canonical case is NVDA: a 4:1 split on
/// 2021-07-20 and a 10:1 split on 2024-06-10, so a share count reported before
/// both must be multiplied by 40 to reach today's basis, and price by 1/40.
/// </summary>
public class SplitAdjustmentTests
{
    private static readonly StockSplit Nvda4For1 = new()
    {
        EffectiveDate = new DateOnly(2021, 7, 20),
        Numerator = 4m,
        Denominator = 1m,
    };

    private static readonly StockSplit Nvda10For1 = new()
    {
        EffectiveDate = new DateOnly(2024, 6, 10),
        Numerator = 10m,
        Denominator = 1m,
    };

    private static StockSplit[] NvdaSplits => [Nvda4For1, Nvda10For1];

    [Fact]
    public void ShareCountFactor_NoSplits_IsOne()
    {
        SplitAdjustment.ShareCountFactor(new DateOnly(2020, 1, 1), []).Should().Be(1m);
    }

    [Fact]
    public void ShareCountFactor_BeforeBothSplits_CompoundsToForty()
    {
        SplitAdjustment.ShareCountFactor(new DateOnly(2021, 1, 1), NvdaSplits).Should().Be(40m);
    }

    [Fact]
    public void ShareCountFactor_BetweenSplits_OnlyLaterSplitApplies()
    {
        SplitAdjustment.ShareCountFactor(new DateOnly(2022, 1, 1), NvdaSplits).Should().Be(10m);
    }

    [Fact]
    public void ShareCountFactor_OnEffectiveDate_TreatsReportAsPostSplit()
    {
        SplitAdjustment.ShareCountFactor(new DateOnly(2024, 6, 10), NvdaSplits).Should().Be(1m);
    }

    [Fact]
    public void ShareCountFactor_AfterAllSplits_IsOne()
    {
        SplitAdjustment.ShareCountFactor(new DateOnly(2025, 1, 1), NvdaSplits).Should().Be(1m);
    }

    [Fact]
    public void PriceFactor_IsInverseOfShareCountFactor()
    {
        SplitAdjustment.PriceFactor(new DateOnly(2021, 1, 1), NvdaSplits).Should().Be(1m / 40m);
    }

    [Fact]
    public void ShareCountFactor_ReverseSplit_ShrinksPreSplitCount()
    {
        StockSplit[] splits =
        [
            new()
            {
                EffectiveDate = new DateOnly(2024, 1, 1),
                Numerator = 1m,
                Denominator = 12m,
            },
        ];

        SplitAdjustment.ShareCountFactor(new DateOnly(2023, 1, 1), splits).Should().Be(1m / 12m);
    }

    [Fact]
    public void ShareCountFactor_ZeroDenominator_IsSkippedNoDivideByZero()
    {
        StockSplit[] splits =
        [
            new()
            {
                EffectiveDate = new DateOnly(2024, 1, 1),
                Numerator = 5m,
                Denominator = 0m,
            },
        ];

        SplitAdjustment.ShareCountFactor(new DateOnly(2023, 1, 1), splits).Should().Be(1m);
    }

    [Fact]
    public void AdjustShareCount_NoSplits_ReturnsCountUnchanged()
    {
        SplitAdjustment.AdjustShareCount(1_000, new DateOnly(2020, 1, 1), []).Should().Be(1_000);
    }

    [Fact]
    public void AdjustShareCount_BeforeBothSplits_RestatesOntoTodaysBasis()
    {
        // 100 pre-split shares × 40 (NVDA 4:1 then 10:1) = 4,000 on today's basis.
        SplitAdjustment
            .AdjustShareCount(100, new DateOnly(2021, 1, 1), NvdaSplits)
            .Should()
            .Be(4_000);
    }

    [Fact]
    public void AdjustShareCount_ReverseSplit_ShrinksAndRoundsToNearestShare()
    {
        StockSplit[] splits =
        [
            new()
            {
                EffectiveDate = new DateOnly(2024, 1, 1),
                Numerator = 1m,
                Denominator = 3m,
            },
        ];

        // 100 / 3 = 33.33… → rounds to the nearest whole share.
        SplitAdjustment.AdjustShareCount(100, new DateOnly(2023, 1, 1), splits).Should().Be(33);
    }

    [Fact]
    public void AdjustShareCount_NegativeCount_PreservesSign()
    {
        // A change/delta can be negative; the 4:1 split still restates it correctly.
        SplitAdjustment
            .AdjustShareCount(-250, new DateOnly(2022, 1, 1), NvdaSplits)
            .Should()
            .Be(-2_500);
    }

    [Fact]
    public void AdjustShareCount_QuarterOverQuarterAcrossASplit_ShowsNoPhantomChange()
    {
        // A 2:1 split falls between two 13F report dates. 1,000 shares held before the split
        // and 2,000 after are the SAME economic position; once both are restated onto today's
        // basis the quarter-over-quarter change must be zero, not +100%.
        StockSplit[] splits =
        [
            new()
            {
                EffectiveDate = new DateOnly(2024, 3, 1),
                Numerator = 2m,
                Denominator = 1m,
            },
        ];

        var priorQuarter = SplitAdjustment.AdjustShareCount(
            1_000,
            new DateOnly(2024, 2, 15),
            splits
        );
        var currentQuarter = SplitAdjustment.AdjustShareCount(
            2_000,
            new DateOnly(2024, 3, 31),
            splits
        );

        priorQuarter.Should().Be(2_000);
        currentQuarter.Should().Be(2_000);
        (currentQuarter - priorQuarter).Should().Be(0, "the split is not a real ownership change");
    }
}
