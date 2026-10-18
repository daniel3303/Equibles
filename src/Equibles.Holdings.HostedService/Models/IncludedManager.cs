namespace Equibles.Holdings.HostedService.Models;

/// <summary>A manager a 13F filing included, with that filing's accession.</summary>
public record IncludedManager(ManagerReference Manager, string AccessionNumber);
