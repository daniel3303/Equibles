namespace Equibles.Yahoo.Repositories;

/// <summary>A listing's newest stored bar date, or null when it has no bars.</summary>
public class ListingLastBar
{
    public Guid EquityListingId { get; set; }

    public DateOnly? Date { get; set; }
}
