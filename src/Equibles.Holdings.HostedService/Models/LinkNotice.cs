namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// A 13F notice with the filer's own numbers and every manager it names as reporting for it, in
/// filed order, whether or not the manager carries an identifier.
/// </summary>
public record LinkNotice(
    string AccessionNumber,
    string FilerCik,
    string Form13FFileNumber,
    string CrdNumber,
    DateOnly ReportDate,
    DateOnly FilingDate,
    IReadOnlyList<ManagerReference> Reporters
);
