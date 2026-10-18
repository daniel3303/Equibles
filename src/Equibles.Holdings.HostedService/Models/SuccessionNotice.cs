namespace Equibles.Holdings.HostedService.Models;

/// <summary>A 13F notice: its filer, quarter and the managers it says report for the filer.</summary>
public record SuccessionNotice(
    string FilerCik,
    string AccessionNumber,
    DateOnly ReportDate,
    IReadOnlyList<ManagerReference> Reporters
);
