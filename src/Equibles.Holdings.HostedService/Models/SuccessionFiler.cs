namespace Equibles.Holdings.HostedService.Models;

/// <summary>A 13F filer's identity numbers and the first and last quarter it filed a 13F report for.</summary>
public record SuccessionFiler(
    Guid HolderId,
    string Cik,
    string Form13FFileNumber,
    string CrdNumber,
    string SecFileNumber,
    DateOnly FirstReportDate,
    DateOnly LastReportDate
);
