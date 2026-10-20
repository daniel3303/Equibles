namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// A 13D/13G primary document read from EDGAR: the parsed filing when <see cref="Status"/> is
/// <see cref="Schedule13DGReadStatus.Parsed"/>, otherwise null.
/// </summary>
public record Schedule13DGDocumentRead(Parsed13DGFiling Filing, Schedule13DGReadStatus Status);
