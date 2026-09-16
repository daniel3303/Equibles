using Equibles.DelayedTrades.Data.Models;

namespace Equibles.DelayedTrades.BusinessLogic.Import;

public enum DelayedTradeIntradayOutcome
{
    NoSession,
    Updated,
    NothingMatched,
}

public sealed record DelayedTradeIntradayResult(
    DelayedTradeIntradayOutcome Outcome,
    DateOnly? SessionDate,
    int PrintCount,
    int MatchedListings,
    int UnmatchedKeys,
    int DroppedAsFresh,
    int RowsWritten
);

public enum DelayedTradeSettleOutcome
{
    NoSession,
    AlreadyImported,
    Imported,
    Rederived,

    // Nothing matched or a write failed: the partition stays unmarked and the next attempt retries the whole session.
    Refused,
}

public sealed record DelayedTradeSettleResult(
    DelayedTradeSettleOutcome Outcome,
    DateOnly? SessionDate,
    DelayedTradeImportPartition Partition,
    string Refusal
);
