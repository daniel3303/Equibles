namespace Equibles.Messaging.Contracts.Activity;

// Published when SEC EDGAR rate-limit-blocks our IP (an HTTP 429 or the
// "Request Rate Threshold Exceeded" page). All SEC requests are then paused for
// ~Pause until the block lifts. Surfaced in the backoffice live-activity feed;
// also available for any other subscriber (alerting, metrics). Not persisted —
// dropped if no consumer is listening.
public record SecRateLimitBlocked(DateTimeOffset Timestamp, TimeSpan Pause, string Url);
