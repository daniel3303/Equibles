namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// One filer's 13F report for a quarter: every manager its filings included, in filing order,
/// and the tracked value of its positions summed over those filings.
/// </summary>
public record SuccessionReport(IReadOnlyList<IncludedManager> Included, long TotalValue);
