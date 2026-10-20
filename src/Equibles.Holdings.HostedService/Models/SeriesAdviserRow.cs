namespace Equibles.Holdings.HostedService.Models;

/// <summary>One investment adviser a current N-CEN report names for a fund series; the CRD is null when the report gives none.</summary>
public record SeriesAdviserRow(
    string SeriesId,
    string RegistrantCik,
    DateOnly ReportEndingPeriod,
    DateOnly FilingDate,
    string AccessionNumber,
    int? Crd
);
