using Equibles.Worker;

namespace Equibles.Sec.FinancialFacts.HostedService.Configuration;

public class ConceptMetadataOptions : ScraperOptions
{
    public ConceptMetadataOptions()
    {
        // The sweep is cheap (a few small JSON fetches per due company) and a
        // new filing should get its concepts documented promptly.
        SleepIntervalHours = 6;
    }

    /// <summary>
    /// How many of the company's most recent filings to read MetaLinks from.
    /// The latest 10-K + a few 10-Qs cover both annual-only and quarterly
    /// concepts; older tags keep whatever text an earlier sweep stored.
    /// </summary>
    public int RecentFilingsPerStock { get; set; } = 4;

    /// <summary>
    /// Re-sweep a company after this many days even without a new filing, so
    /// taxonomy-text refinements eventually propagate.
    /// </summary>
    public int RefreshDays { get; set; } = 90;
}
