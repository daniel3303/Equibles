namespace Equibles.Integrations.Common.RateLimiter;

public interface IRateLimiter
{
    Task WaitAsync();
    void PauseFor(TimeSpan duration);

    // True while a PauseFor window is still in effect. The single source of truth
    // for "are we currently throttled" — callers should not keep a separate flag.
    bool IsThrottled { get; }
}
