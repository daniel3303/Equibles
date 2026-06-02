namespace Equibles.Integrations.Common.Retry;

public static class RetryBackoff
{
    // Exponential backoff between HTTP retries: f(n) = 2^(n+1) seconds
    // (f(0)=2s, f(1)=4s, f(2)=8s, f(3)=16s). The +1 shift starts the first
    // retry at 2s — dropping it would halve every interval and turn an
    // upstream throttle into a hard ban. Centralised so no client's copy drifts.
    public static TimeSpan Exponential(int attempt) =>
        TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
}
