namespace Equibles.Holdings.Repositories.Models;

/// <summary>A filer with at least one Schedule 13D or 13G stake reported at or above the threshold, and when it last filed one.</summary>
public class Schedule13DGHolderActivity
{
    public Guid InstitutionalHolderId { get; set; }
    public DateOnly LastFilingDate { get; set; }
}
