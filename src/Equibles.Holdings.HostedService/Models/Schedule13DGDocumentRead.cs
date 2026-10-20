namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// A 13D/13G primary document read from EDGAR: the parsed filing, or null when EDGAR does not serve
/// it or it cannot be parsed. <see cref="Outage"/> marks a read that failed because EDGAR is unavailable.
/// </summary>
public record Schedule13DGDocumentRead(Parsed13DGFiling Filing, bool Outage);
