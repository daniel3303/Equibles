namespace Equibles.InsiderTrading.Data.Models;

/// <summary>
/// The relationship boxes a reporting owner ticked on one Form 3/4/5
/// (<c>reportingOwnerRelationship</c>). Stored per filing because one owner can be a
/// director at one issuer and only a 10% holder at another.
/// </summary>
[Flags]
public enum InsiderRelationship
{
    None = 0,
    Director = 1,
    Officer = 2,
    TenPercentOwner = 4,
    Other = 8,
}
