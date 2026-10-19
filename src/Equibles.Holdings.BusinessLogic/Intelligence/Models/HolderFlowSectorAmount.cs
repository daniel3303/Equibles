using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>Buying and selling in one sector, or in exchange-traded products or unclassified.</summary>
public sealed record HolderFlowSectorAmount
{
    public HolderFlowSectorGroup Group { get; init; }
    public Guid? SectorId { get; init; }
    public string Name { get; init; }
    public decimal BuyValue { get; init; }
    public decimal SellValue { get; init; }
    public decimal NetValue => BuyValue - SellValue;
}
