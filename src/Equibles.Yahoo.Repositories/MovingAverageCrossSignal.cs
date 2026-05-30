using System.ComponentModel.DataAnnotations;

namespace Equibles.Yahoo.Repositories;

/// <summary>
/// The most recent moving-average crossover within a lookback window. A golden
/// cross (shorter average rising above the longer one) is a bullish signal; a
/// death cross (shorter average falling below the longer one) is bearish.
/// </summary>
public enum MovingAverageCrossSignal
{
    [Display(Name = "None")]
    None,

    [Display(Name = "Golden Cross")]
    GoldenCross,

    [Display(Name = "Death Cross")]
    DeathCross,
}
