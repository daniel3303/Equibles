namespace Equibles.Congress.HostedService.Models;

/// <summary>
/// A trade-disclosure fetch pass: the parsed transactions plus the filings
/// that were fully handled and may be marked as ingested once the
/// transactions are committed.
/// </summary>
public class DisclosureFetchResult
{
    public List<DisclosureTransaction> Transactions { get; set; } = [];
    public List<ProcessedFiling> ProcessedFilings { get; set; } = [];
}
