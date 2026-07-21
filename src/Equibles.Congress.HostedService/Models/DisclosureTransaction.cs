using Equibles.Congress.Data.Models;

namespace Equibles.Congress.HostedService.Models;

public class DisclosureTransaction
{
    // The filing this transaction came from (House DocID / Senate report
    // GUID) — lets the sync tie persistence outcomes back to the filing when
    // deciding whether to mark it as ingested.
    public string SourceId { get; set; }

    public required string MemberName { get; init; }
    public CongressPosition Position { get; init; }
    public string Ticker { get; init; }
    public string AssetName { get; init; }
    public DateOnly TransactionDate { get; init; }
    public DateOnly FilingDate { get; init; }
    public CongressTransactionType TransactionType { get; init; }
    public string OwnerType { get; init; }
    public long AmountFrom { get; init; }
    public long AmountTo { get; init; }
}
