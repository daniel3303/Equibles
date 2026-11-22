namespace Equibles.Sec.HostedService.Models;

public class FundReportPeriodBackfillResult
{
    public int Selected { get; set; }
    public int Dated { get; set; }
    public int Unreadable { get; set; }
}
