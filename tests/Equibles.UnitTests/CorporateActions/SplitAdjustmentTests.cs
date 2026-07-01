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
}
