namespace Equibles.Finra.BusinessLogic.Models;

/// <summary>
/// One daily price bar of the minimal shape the squeeze price factors need.
/// <see cref="Close"/> is the raw as-traded close inside one captured-split
/// interval selected by the caller; its product with <see cref="Volume"/> is
/// that day's dollar turnover on the same basis.
/// </summary>
public readonly record struct ShortSqueezeDailyBar(DateOnly Date, decimal Close, long Volume);
