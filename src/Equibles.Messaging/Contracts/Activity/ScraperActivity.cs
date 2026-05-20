namespace Equibles.Messaging.Contracts.Activity;

// Human-readable "what is this scraper doing right now" event. Carried over the
// shared MassTransit transport so the web portal can fan it out to connected
// browsers (live activity feed). Not persisted — messages with no listeners are
// dropped. Use Severity.Info for normal lifecycle events, Warn for skips /
// throttling / backoff, Error for failures.
public record ScraperActivity(
    string Source,
    ScraperActivitySeverity Severity,
    string Message,
    DateTimeOffset Timestamp,
    string CorrelationId
);
