namespace Equibles.Holdings.HostedService.Models;

/// <summary>A 13F filer's own filed identity numbers and the Form ADV CRD its profile resolved.</summary>
public record LinkHolder(
    Guid HolderId,
    string Cik,
    string Form13FFileNumber,
    string CrdNumber,
    int? AdviserCrd
);
