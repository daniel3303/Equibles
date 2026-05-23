namespace Equibles.Holdings.Repositories.Models;

public class AumSnapshot
{
    public DateOnly ReportDate { get; set; }
    public long TotalValue { get; set; }
    public int FilerCount { get; set; }
    public int PositionCount { get; set; }
}
