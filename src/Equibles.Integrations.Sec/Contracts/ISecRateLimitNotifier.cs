namespace Equibles.Integrations.Sec.Contracts;

/// <summary>
/// Hook for surfacing SEC rate-limit state changes (e.g. publishing a bus
/// event). <see cref="SecEdgarClient"/> calls <see cref="RateLimited"/> whenever
/// SEC throttles us (a 429 or the "Request Rate Threshold Exceeded" page) and
/// <see cref="Reachable"/> whenever a request succeeds; the implementation
/// decides what to do with those edges. Integrations.Sec has no messaging
/// dependency, so the default is a no-op (<see cref="NullSecRateLimitNotifier"/>)
/// and a higher layer (the worker) supplies a publishing implementation.
/// </summary>
public interface ISecRateLimitNotifier
{
    Task RateLimited(TimeSpan pause, string url);

    Task Reachable(string url);
}
