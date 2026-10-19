using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>Where a security's trades land when flows are split by sector.</summary>
public readonly record struct HolderFlowSector(HolderFlowSectorGroup Group, Guid? SectorId)
{
    public static readonly HolderFlowSector ExchangeTraded = new(
        HolderFlowSectorGroup.ExchangeTraded,
        null
    );

    public static readonly HolderFlowSector Unclassified = new(
        HolderFlowSectorGroup.Unclassified,
        null
    );
}
