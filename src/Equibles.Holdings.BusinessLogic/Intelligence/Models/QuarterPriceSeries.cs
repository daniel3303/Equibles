namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>
/// One listing's traded sessions in a quarter as stored, plus what is needed to put them on the
/// quarter-end filing basis and to check that basis against 13F filers' own valuations.
/// </summary>
public sealed record QuarterPriceSeries
{
    /// <summary>The mean stored close over the quarter's sessions with volume.</summary>
    public decimal AverageClose { get; init; }

    /// <summary>The stored close of the quarter's last session with volume.</summary>
    public decimal LastClose { get; init; }

    public DateOnly LastSessionDate { get; init; }
    public int Sessions { get; init; }

    /// <summary>
    /// The factor that moves a quarter-end share count onto the stored series' basis; null when a
    /// later split's basis could not be resolved.
    /// </summary>
    public decimal? ShareCountFactor { get; init; }

    /// <summary>True when one of the series' splits took effect inside the quarter.</summary>
    public bool SplitInsideQuarter { get; init; }

    /// <summary>
    /// Every 13F filer's reported value divided by reported shares at the quarter end; null when
    /// no filer valued the security.
    /// </summary>
    public decimal? FiledPricePerShare { get; init; }
}
