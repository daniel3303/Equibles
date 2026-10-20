using Equibles.Holdings.Data.Models;
using Equibles.Sec.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Advisers;

/// <summary>
/// Decides an adviser's <see cref="PrivateFundFocus"/>: by gross asset value from a Schedule D
/// 7.B.(1) filing at most <see cref="ScheduleDLookbackMonths"/> old, since a few large funds
/// outweigh many small ones; otherwise by fund count from the monthly roster.
/// </summary>
public static class AdviserPrivateFundFocus
{
    public const int ScheduleDLookbackMonths = 24;

    /// <summary>The focus, what decided it and its as-of date; None with no basis when no funds are listed.</summary>
    public static (PrivateFundFocus Focus, PrivateFundFocusBasis? Basis, DateOnly? AsOf) Decide(
        RosterFundCounts roster,
        DateOnly rosterDate,
        FormAdvPrivateFundFiling scheduleD,
        DateOnly asOf
    )
    {
        if (
            scheduleD is { FundCount: > 0, GrossAssets: > 0 }
            && scheduleD.FiledOn <= asOf
            && scheduleD.FiledOn >= asOf.AddMonths(-ScheduleDLookbackMonths)
        )
            return (ByAssets(scheduleD), PrivateFundFocusBasis.GrossAssets, scheduleD.FiledOn);
        var byCount = ByCount(roster);
        return byCount == PrivateFundFocus.None
            ? (PrivateFundFocus.None, null, null)
            : (byCount, PrivateFundFocusBasis.FundCount, rosterDate);
    }

    /// <summary>The type that is more than half of the funds by count.</summary>
    public static PrivateFundFocus ByCount(RosterFundCounts roster) =>
        Majority([
            (PrivateFundFocus.Hedge, roster.Hedge ?? 0),
            (PrivateFundFocus.PrivateEquity, roster.PrivateEquity ?? 0),
            (PrivateFundFocus.VentureCapital, roster.VentureCapital ?? 0),
            (PrivateFundFocus.RealEstate, roster.RealEstate ?? 0),
            (PrivateFundFocus.Liquidity, roster.Liquidity ?? 0),
            (PrivateFundFocus.Securitized, roster.Securitized ?? 0),
            (PrivateFundFocus.Other, roster.Other ?? 0),
        ]);

    /// <summary>The type that is more than half of the funds' gross asset value.</summary>
    public static PrivateFundFocus ByAssets(FormAdvPrivateFundFiling filing) =>
        Majority([
            (PrivateFundFocus.Hedge, filing.HedgeFundGrossAssets),
            (PrivateFundFocus.PrivateEquity, filing.PrivateEquityFundGrossAssets),
            (PrivateFundFocus.VentureCapital, filing.VentureCapitalFundGrossAssets),
            (PrivateFundFocus.RealEstate, filing.RealEstateFundGrossAssets),
            (PrivateFundFocus.Liquidity, filing.LiquidityFundGrossAssets),
            (PrivateFundFocus.Securitized, filing.SecuritizedAssetFundGrossAssets),
            (PrivateFundFocus.Other, filing.OtherPrivateFundGrossAssets),
        ]);

    private static PrivateFundFocus Majority((PrivateFundFocus Focus, long Amount)[] amounts)
    {
        var total = amounts.Sum(item => item.Amount);
        if (total <= 0)
            return PrivateFundFocus.None;
        var majority = amounts.FirstOrDefault(item => item.Amount * 2 > total);
        return majority.Amount > 0 ? majority.Focus : PrivateFundFocus.Mixed;
    }
}
