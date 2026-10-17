namespace Equibles.Holdings.HostedService.Models;

/// <summary>An adviser's registered fund series as of each series' latest N-CEN report.</summary>
public record AdvisedSeriesSummary(
    int SeriesCount,
    int IndexSeriesCount,
    int EtfSeriesCount,
    decimal? NetAssets,
    decimal? IndexNetAssets,
    DateOnly AsOf
);
