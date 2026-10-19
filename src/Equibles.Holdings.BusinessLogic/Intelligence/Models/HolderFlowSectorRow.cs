using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>One filer's stored buying and selling in one sector group.</summary>
public sealed record HolderFlowSectorRow(
    Guid InstitutionalHolderId,
    HolderFlowSectorGroup Group,
    Guid? SectorId,
    decimal BuyValue,
    decimal SellValue
);
