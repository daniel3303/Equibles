using Equibles.Web.Extensions;

namespace Equibles.Web.ViewModels.Shared;

public abstract class StatsViewModel
{
    public decimal? Mean { get; set; }
    public decimal? Median { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public decimal? StdDev { get; set; }

    public void ApplyStats(StatsSummary stats)
    {
        Mean = stats.Mean;
        Median = stats.Median;
        Min = stats.Min;
        Max = stats.Max;
        StdDev = stats.StdDev;
    }
}
