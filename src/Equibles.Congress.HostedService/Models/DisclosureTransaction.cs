using Equibles.Congress.Data.Models;

namespace Equibles.Congress.HostedService.Models;

public class DisclosureTransaction
{
    // The filing this transaction came from (House DocID / Senate report
    // GUID) — lets the sync tie persistence outcomes back to the filing when
    // deciding whether to mark it as ingested.
    public string SourceId { get; set; }

    // The seat the filer holds, as the House Clerk's index states it ("SC05").
    // Stamped from the filing alongside SourceId rather than parsed out of the
    // PDF, which never names it. Null on Senate transactions — no Senate
    // filing states the member's state.
    public string StateDistrict { get; set; }

    public required string MemberName { get; init; }
    public CongressPosition Position { get; init; }
    public string Ticker { get; init; }
    public string AssetName { get; init; }
    public DateOnly TransactionDate { get; init; }
    public DateOnly FilingDate { get; init; }
    public CongressTransactionType TransactionType { get; init; }
    public string OwnerType { get; init; }
    public string AssetType { get; init; }
    public string Subholding { get; init; }
    public long AmountFrom { get; init; }
    public long AmountTo { get; init; }
}
