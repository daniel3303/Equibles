namespace Equibles.Integrations.Sec.Models;

public class FilingData {
    public string Cik { get; set; }
    public string AccessionNumber { get; set; }
    public DateOnly FilingDate { get; set; }
    public DateOnly ReportDate { get; set; }
    public string Form { get; set; }
    public string PrimaryDocument { get; set; }
    public string Description { get; set; }
    public string DocumentUrl { get; set; }
}