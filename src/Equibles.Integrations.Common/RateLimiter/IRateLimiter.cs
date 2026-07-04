namespace Equibles.Integrations.Common.RateLimiter;

public interface IRateLimiter
{
    // The token matters most during a PauseFor penalty window (SEC blocks run 10
    // minutes): an uncancellable wait there holds worker shutdown hostage for the
    // whole pause.
    Task WaitAsync(CancellationToken cancellationToken = default);
    void PauseFor(TimeSpan duration);

    // True while a PauseFor window is still in effect. The single source of truth
    // for "are we currently throttled" — callers should not keep a separate flag.
    bool IsThrottled { get; }
}
