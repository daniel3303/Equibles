namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// Outcome of a real-time sweep (13F or 13D/G). <see cref="EarliestFailedDate"/>
/// is the oldest date this cycle left retryable work behind — a daily index that
/// could not be fetched (SEC throttling or a transient error), or the filing
/// date of a filing whose import threw — or null if the window swept cleanly.
/// The worker uses it to hold the sweep watermark back so the failed work is
/// re-swept next cycle, even after it ages out of the trailing window.
/// </summary>
public record RealtimeIngestionResult(int FilingsImported, DateOnly? EarliestFailedDate);
