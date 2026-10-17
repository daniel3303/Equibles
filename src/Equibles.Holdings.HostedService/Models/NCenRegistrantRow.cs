namespace Equibles.Holdings.HostedService.Models;

/// <summary>A registrant's latest N-CEN report.</summary>
public record NCenRegistrantRow(
    string Cik,
    string AccessionNumber,
    DateOnly ReportEndingPeriod,
    string InvestmentCompanyType
);
