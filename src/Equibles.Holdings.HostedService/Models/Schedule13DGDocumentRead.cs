namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// A 13D/13G document read from EDGAR: the parsed filing when <see cref="Status"/> is
/// <see cref="Schedule13DGReadStatus.Parsed"/>, the refused text read when it is
/// <see cref="Schedule13DGReadStatus.Refused"/>, otherwise null.
/// </summary>
public record Schedule13DGDocumentRead(
    Parsed13DGFiling Filing,
    Schedule13DGReadStatus Status,
    Schedule13DGTextParse Refusal = null
);
