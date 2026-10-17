using Equibles.Sec.Data.Models;

namespace Equibles.Holdings.HostedService.Models;

/// <summary>A registered fund series an adviser manages as primary adviser, from one N-CEN report.</summary>
public record AdvisedSeriesRow(
    int Crd,
    string SeriesKey,
    DateOnly ReportEndingPeriod,
    NCenFundTypes FundTypes,
    decimal? NetAssets
);
