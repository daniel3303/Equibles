namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>
/// One listed security of an issuer: a null <see cref="ListedTicker"/> is the primary listing.
/// Sibling share classes are different securities, so trades in one never net against another.
/// </summary>
public readonly record struct HolderSecurityKey(Guid IssuerId, string ListedTicker);
