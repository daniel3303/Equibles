namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>One security's priced trade between two quarters; one side is always zero.</summary>
public readonly record struct HolderSecurityTrade(
    HolderSecurityKey Security,
    decimal BuyValue,
    decimal SellValue
);
