namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// Outcome of a real-time 13F sweep. <see cref="EarliestFailedDate"/> is the
/// oldest daily-index date that could not be fetched this cycle (SEC throttling
/// or a transient error), or null if every day in the window swept cleanly —
/// the worker uses it to hold the sweep watermark back so failed days are
/// re-swept next cycle.
/// </summary>
public record RealtimeIngestionResult(int FilingsImported, DateOnly? EarliestFailedDate);
