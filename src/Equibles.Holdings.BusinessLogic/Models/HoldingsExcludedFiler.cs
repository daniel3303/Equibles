namespace Equibles.Holdings.BusinessLogic.Models;

// A filer left out of a like-for-like comparison, with its restated shares on the side where it
// holds the stock. Name may be null when only the share total is needed.
public sealed record HoldingsExcludedFiler(Guid InstitutionalHolderId, string Name, long Shares);
