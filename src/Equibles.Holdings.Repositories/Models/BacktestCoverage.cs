namespace Equibles.Holdings.Repositories.Models;

/// <summary>
/// How much of a filer's reported 13F book the clone simulation actually tracks.
/// </summary>
/// <remarks>
/// <para>
/// A clone is long-only: option legs are notional, cannot be replicated by buying the underlying,
/// and are skipped. For most filers that discards a rounding error. For a filer who expresses its
/// thesis in options it discards the thesis, and what remains is a handful of residual equity
/// positions that had nothing to do with the manager's actual bet — yet the result is still
/// presented as "cloning this manager".
/// </para>
/// <para>
/// Michael Burry's Scion is the extreme case: across a trailing three years its filings are 80–90%
/// put notional, and in Q1 2025 the entire long book was a single $13M Estée Lauder position beside
/// $109M of puts. The clone rode that one stock to +41% for the quarter, which is over half of the
/// +102.5% three-year headline the page advertised — during a period when the manager's real,
/// options-expressed return was negative. Nothing in the arithmetic was wrong; the number simply
/// was not a description of Burry.
/// </para>
/// <para>
/// So the simulation reports what it covered. A caller that cannot show this must not show the
/// headline either.
/// </para>
/// </remarks>
public class BacktestCoverage
{
    /// <summary>
    /// Mean share of reported value that was long equity, across the snapshots the simulation
    /// rebalanced on. Each quarter counts once: a quarter is one rebalance decision, so weighting
    /// by portfolio size would let a single huge quarter speak for the rest.
    /// </summary>
    public decimal AverageLongPercent { get; set; }

    /// <summary>
    /// The worst single quarter. Carried separately because an average hides the case that matters
    /// most — a book that is fully long for two years and then 4% long for the stretch that
    /// produced the return.
    /// </summary>
    public decimal MinimumLongPercent { get; set; }

    /// <summary>Quarters with any reported value, i.e. how many the two figures above are over.</summary>
    public int QuartersMeasured { get; set; }

    /// <summary>
    /// The average below which a clone stops being a description of the manager. Set where a
    /// meaningful minority of the book is already untracked; Scion's trailing three years average
    /// about 58%, and that result was not Burry's.
    /// </summary>
    public const decimal RepresentativeAveragePercent = 60m;

    /// <summary>
    /// The single-quarter floor. A quarter this thinly covered contributes a return drawn from
    /// whatever equity happened to be left over, which is how one $13M residual position came to
    /// supply half of a three-year headline.
    /// </summary>
    public const decimal RepresentativeMinimumPercent = 50m;

    /// <summary>
    /// Whether the simulated return can honestly be presented as this manager's. Both bounds must
    /// hold: an average alone would pass a book that was fully long for two years and 4% long for
    /// the stretch that produced the return. A surface showing the headline without checking this
    /// is asserting something the data does not support.
    /// </summary>
    public bool IsRepresentative =>
        QuartersMeasured > 0
        && AverageLongPercent >= RepresentativeAveragePercent
        && MinimumLongPercent >= RepresentativeMinimumPercent;
}
