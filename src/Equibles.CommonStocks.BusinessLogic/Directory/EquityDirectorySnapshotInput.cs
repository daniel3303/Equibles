namespace Equibles.CommonStocks.BusinessLogic.Directory;

// A complete authoritative market directory, validated by its source adapter before reconciliation.
public class EquityDirectorySnapshotInput
{
    public string Source { get; set; }
    public string EvidenceSource { get; set; }
    public string SourceRecordKey { get; set; }
    public string PayloadJson { get; set; }
    public DateTime ObservedAt { get; set; }
    public string MarketCountryCode { get; set; }
    public IReadOnlyList<string> MarketIdentifierCodes { get; set; } = [];
    public IReadOnlyList<EquityDirectoryListingKey> Listings { get; set; } = [];

    // Which half of the market the capture states: a venue's share list and its exchange-traded product list are
    // separate captures, and each withdraws only its own absentees, never the other's listings.
    public bool ExchangeTradedProducts { get; set; }
}
