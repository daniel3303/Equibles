namespace Equibles.Holdings.Repositories.Models;

public class SectorAllocationSnapshot
{
    public DateOnly ReportDate { get; set; }
    public Guid? SectorId { get; set; }
    public string SectorName { get; set; }
    public long TotalValue { get; set; }
}
