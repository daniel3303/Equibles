namespace Equibles.Finra.BusinessLogic.Models;

/// <summary>
/// One daily price bar of the minimal shape the squeeze price factors need.
/// <see cref="AdjustedClose"/> is the split- and dividend-adjusted close (a
/// comparable series across time); <see cref="Close"/> and <see cref="Volume"/>
/// are the raw as-traded figures for the day, whose product is that day's
/// dollar turnover on a self-consistent basis regardless of later splits.
/// </summary>
public readonly record struct ShortSqueezeDailyBar(
    DateOnly Date,
    decimal AdjustedClose,
    decimal Close,
    long Volume
);
