namespace Equibles.Web.ViewModels.Profiles;

public class HoldingRowViewModel
{
    public string Ticker { get; set; }
    public string Company { get; set; }
    public DateOnly ReportDate { get; set; }
    public long Shares { get; set; }
    public long Value { get; set; }
}
