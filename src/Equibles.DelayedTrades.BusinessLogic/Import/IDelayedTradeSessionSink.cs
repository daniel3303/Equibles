namespace Equibles.DelayedTrades.BusinessLogic.Import;

/// <summary>
/// Optional consumer of a settled session's own prints, for a deployment that derives more from
/// them than the daily bar. Nothing is registered by default. Every registered sink is offered a
/// session each time a settle pass finds it settled, whether that pass wrote it, re-derived it or
/// found it already marked, so a sink decides for itself whether it already holds the session.
/// One that throws never fails the settle, which names it in
/// <see cref="DelayedTradeSettleResult.SinkFailures"/>, and the settle is not repeated for it: the
/// session comes again only with the next day's re-derivation or after a restart, while the venue
/// still serves the file, and is gone for that sink once the file rotates.
/// </summary>
public interface IDelayedTradeSessionSink
{
    Task Accept(DelayedTradeSettledSession session, CancellationToken cancellationToken);
}
