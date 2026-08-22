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

    // False when any index/report in the requested range could not be fetched or parsed. Archive
    // windows are checkpointed only when this remains true, so transient failures retry the year.
    public bool IsComplete { get; set; } = true;
}
