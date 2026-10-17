using Equibles.Sec.Data.Models;

namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// A registered fund series and one primary adviser named on one N-CEN report; the CRD is null when
/// the report gives none.
/// </summary>
public record AdvisedSeriesRow(
    int? Crd,
    string SeriesKey,
    DateOnly ReportEndingPeriod,
    NCenFundTypes FundTypes,
    decimal? NetAssets
);
