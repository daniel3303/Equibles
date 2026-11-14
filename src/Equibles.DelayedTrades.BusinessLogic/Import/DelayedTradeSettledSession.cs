using Equibles.DelayedTrades.Repositories;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.Integrations.DelayedTrades;

namespace Equibles.DelayedTrades.BusinessLogic.Import;

/// <summary>
/// One market session whose daily bars are settled, as offered to an
/// <see cref="IDelayedTradeSessionSink"/>. <see cref="Outcome"/> says what the settle pass did:
/// <see cref="DelayedTradeSettleOutcome.Imported"/> and
/// <see cref="DelayedTradeSettleOutcome.Rederived"/> mean the bars were just derived from the
/// file being served, <see cref="DelayedTradeSettleOutcome.AlreadyImported"/> that an earlier
/// pass derived them.
/// </summary>
public sealed class DelayedTradeSettledSession(
    EquityMarket market,
    DateOnly sessionDate,
    TimeZoneInfo zone,
    DelayedTradeSettleOutcome outcome,
    Func<CancellationToken, Task<IEnumerable<DelayedTradeSessionPrint>>> readPrints
)
{
    public EquityMarket Market { get; } = market;

    public DateOnly SessionDate { get; } = sessionDate;

    /// <summary>The market's clock, which decides the session a print belongs to.</summary>
    public TimeZoneInfo Zone { get; } = zone;

    public DelayedTradeSettleOutcome Outcome { get; } = outcome;

    /// <summary>
    /// The session's prints by the rules the daily bars count them with: effective after
    /// cancellations and amendments, real trades in money terms, matched to one verified listing
    /// in its own quotation unit, traded on <see cref="SessionDate"/>. A listing whose prints
    /// form no price has no daily bar, and its prints are read here all the same. The file is
    /// parsed only when this is called, and again on each enumeration of the result, which
    /// streams in the file's publication order and never in trade-time order.
    /// </summary>
    public Task<IEnumerable<DelayedTradeSessionPrint>> ReadPrints(
        CancellationToken cancellationToken
    ) => readPrints(cancellationToken);
}

/// <summary>A counted print and the verified listing it was matched to.</summary>
public readonly record struct DelayedTradeSessionPrint(
    DelayedTradeListingReference Listing,
    DelayedTradePrint Print
);
