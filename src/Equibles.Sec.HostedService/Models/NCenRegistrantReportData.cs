namespace Equibles.Sec.HostedService.Models;

/// <summary>One N-CEN or N-CEN/A accession of a data set, with its funds, as filed.</summary>
public class NCenRegistrantReportData
{
    public string AccessionNumber { get; set; }
    public string Cik { get; set; }
    public bool IsAmendment { get; set; }
    public DateOnly FilingDate { get; set; }
    public DateOnly ReportEndingPeriod { get; set; }
    public string RegistrantName { get; set; }
    public string InvestmentCompanyFileNumber { get; set; }
    public string Lei { get; set; }
    public string InvestmentCompanyType { get; set; }
    public bool? IsFamilyInvestmentCompany { get; set; }
    public string FamilyInvestmentCompanyName { get; set; }
    public int? TotalSeries { get; set; }
    public List<NCenFundData> Funds { get; set; } = [];
}
