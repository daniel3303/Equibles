namespace Equibles.Integrations.Common.RateLimiter;

public interface IRateLimiter {
    Task WaitAsync();
    void PauseFor(TimeSpan duration);
}