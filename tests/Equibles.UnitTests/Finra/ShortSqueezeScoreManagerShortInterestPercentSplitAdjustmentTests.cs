using System.Reflection;
using Equibles.CorporateActions.Data.Models;
using Equibles.Finra.BusinessLogic;

namespace Equibles.UnitTests.Finra;

/// <summary>
/// Pins that ShortSqueezeScoreManager restates the short position — a share COUNT observed
/// as-of the FINRA settlement date — onto today's split basis before dividing it by the
/// CURRENT shares outstanding. A stock that split after the settlement date must report its
/// short interest as a fraction of shares on the same basis as the denominator; without the
/// restatement a 10:1 split makes the raw ratio 10× too small (and the peer-relative rank
/// collapses the stock out of the squeeze board).
/// </summary>
public class ShortSqueezeScoreManagerShortInterestPercentSplitAdjustmentTests
{
    private static readonly StockSplit TenForOne = new()
    {
        EffectiveDate = new DateOnly(2024, 6, 10),
        Numerator = 10m,
        Denominator = 1m,
    };

    [Fact]
    public void ShortInterestPercentOfShares_SplitAfterSettlement_RestatedOntoCurrentBasis()
    {
        // 1,000,000 shares short as-of a settlement BEFORE a 10:1 split, against today's
        // 100,000,000 shares outstanding. The position must be scaled ×10 to 10,000,000
        // before dividing, giving 10% — not the un-restated 1%.
        var settlementDate = new DateOnly(2024, 5, 15);

        var result = InvokeShortInterestPercentOfShares(
            currentShortPosition: 1_000_000,
            sharesOutstanding: 100_000_000,
            settlementDate,
            [TenForOne]
        );

        result.Should().Be(0.10m);
    }

    [Fact]
    public void ShortInterestPercentOfShares_NoSplits_LeavesRatioUnchanged()
    {
        var result = InvokeShortInterestPercentOfShares(
            currentShortPosition: 5_000_000,
            sharesOutstanding: 100_000_000,
            new DateOnly(2024, 5, 15),
            []
        );

        result.Should().Be(0.05m);
    }

    [Fact]
    public void ShortInterestPercentOfShares_SettlementAfterSplit_NoAdjustment()
    {
        // A settlement dated after the split already reports the post-split count, so the
        // factor is 1 and the ratio is the raw one.
        var result = InvokeShortInterestPercentOfShares(
            currentShortPosition: 10_000_000,
            sharesOutstanding: 100_000_000,
            new DateOnly(2024, 7, 1),
            [TenForOne]
        );

        result.Should().Be(0.10m);
    }

    private static decimal InvokeShortInterestPercentOfShares(
        long currentShortPosition,
        long sharesOutstanding,
        DateOnly settlementDate,
        IReadOnlyList<StockSplit> splits
    )
    {
        var method = typeof(ShortSqueezeScoreManager).GetMethod(
            "ShortInterestPercentOfShares",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        method.Should().NotBeNull("the split-adjusted short-interest-percent helper should exist");
        return (decimal)
            method!.Invoke(null, [currentShortPosition, sharesOutstanding, settlementDate, splits]);
    }
}
