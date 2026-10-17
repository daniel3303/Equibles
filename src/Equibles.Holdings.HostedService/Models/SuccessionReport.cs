namespace Equibles.Holdings.HostedService.Models;

/// <summary>One filer's 13F report for a quarter and the managers it included.</summary>
public record SuccessionReport(string AccessionNumber, IReadOnlyList<ManagerReference> Included);
