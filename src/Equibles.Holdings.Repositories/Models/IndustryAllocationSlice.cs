namespace Equibles.Holdings.Repositories.Models;

public class IndustryAllocationSlice
{
    // null when the slice represents holdings without an IndustryId — see UnclassifiedName.
    public Guid? IndustryId { get; set; }

    public string IndustryName { get; set; }
    public int PositionCount { get; set; }
    public long TotalValue { get; set; }
    public double PercentOfPortfolio { get; set; }

    public const string UnclassifiedName = "Unclassified";
}
