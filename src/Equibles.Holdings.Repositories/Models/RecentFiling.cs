namespace Equibles.Holdings.Repositories.Models;

public class RecentFiling
{
    public string AccessionNumber { get; set; }
    public Guid InstitutionalHolderId { get; set; }
    public string FilerName { get; set; }
    public string FilerCik { get; set; }
    public DateOnly FilingDate { get; set; }
    public DateOnly ReportDate { get; set; }
    public int PositionCount { get; set; }
    public long TotalValue { get; set; }
    public bool IsAmendment { get; set; }
    public DateTime ImportedAt { get; set; }
    public bool IsNewFiler { get; set; }
}
