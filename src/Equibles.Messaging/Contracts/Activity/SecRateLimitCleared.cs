namespace Equibles.Messaging.Contracts.Activity;

// Published when SEC EDGAR becomes reachable again after a rate-limit block —
// the first successful request following a SecRateLimitBlocked. Surfaced in the
// backoffice live-activity feed. Not persisted — dropped if no consumer is
// listening.
public record SecRateLimitCleared(DateTimeOffset Timestamp, string Url);
