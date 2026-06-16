namespace Equibles.Sec.HostedService.Models;

/// <summary>
/// Outcome of one NPORT-P daily-index sweep cycle.
/// </summary>
/// <param name="Stored">Trust-only filings stored this cycle (with or without tracked holdings).</param>
/// <param name="Examined">Candidate filings fetched and classified this cycle.</param>
/// <param name="MoreWorkQueued">
/// True when the cycle hit its per-cycle candidate cap and unprocessed candidates remain, so the
/// worker should continue promptly instead of sleeping the full interval.
/// </param>
/// <param name="NotReady">
/// True when the tracked-stock universe is not populated yet (cold start), so there is nothing to
/// match holdings against and the worker should retry soon rather than record progress.
/// </param>
public record NportRealtimeResult(int Stored, int Examined, bool MoreWorkQueued, bool NotReady);
