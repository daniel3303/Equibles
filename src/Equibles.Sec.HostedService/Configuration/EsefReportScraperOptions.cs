using Equibles.Worker;

namespace Equibles.Sec.HostedService.Configuration;

public class EsefReportScraperOptions : ScraperOptions
{
    /// <summary>
    /// How many reports one cycle downloads. A report ran to a median of 17.6 MB across twelve markets and
    /// the tail to 125 MB, so the first pass over a few thousand issuers drains across days rather than
    /// pulling tens of gigabytes in one cycle.
    /// </summary>
    public int MaxCapturesPerCycle { get; set; } = 100;
}
