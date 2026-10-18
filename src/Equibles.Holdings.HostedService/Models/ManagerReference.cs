namespace Equibles.Holdings.HostedService.Models;

/// <summary>Another manager as a filing names it, by any of its filed identifiers.</summary>
public record ManagerReference(
    string Cik,
    string Form13FFileNumber,
    string CrdNumber,
    string SecFileNumber
);
