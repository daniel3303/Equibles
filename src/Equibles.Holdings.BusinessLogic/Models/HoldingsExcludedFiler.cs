namespace Equibles.Holdings.BusinessLogic.Models;

// A filer left out of a like-for-like comparison, with its restated shares on the side where it
// holds the stock. Name may be null when only the share total is needed.
public sealed record HoldingsExcludedFiler(Guid InstitutionalHolderId, string Name, long Shares)
{
    // Loaded with the name; null when only the share total is needed.
    public string Cik { get; init; }

    // The quarter Shares was disclosed for, when the filer is listed for a quarter it withheld.
    public DateOnly? DisclosedFor { get; init; }
}
