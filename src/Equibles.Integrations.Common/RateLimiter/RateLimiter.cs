namespace Equibles.Integrations.Common.RateLimiter;

public class RateLimiter : IRateLimiter
{
    private readonly int _maxRequests;
    private readonly TimeSpan _timeWindow;
    private readonly Queue<DateTime> _requestTimes;
    private readonly Queue<DateTime> _urgentTimes = new();
    private readonly Queue<DateTime> _backgroundTimes = new();
    private readonly int _backgroundReserve;
    private readonly Lock _lock = new();
    private DateTime _pauseUntil = DateTime.MinValue;
    private int _urgentWaiting;
    private int _backgroundWaiting;

    // How often a caller that is yielding to the other class looks again; the window itself
    // still has room, so there is no expiry to wait for.
    private static readonly TimeSpan YieldInterval = TimeSpan.FromMilliseconds(100);

    /// <param name="backgroundReserve">
    /// Requests per window kept for background callers while urgent ones are waiting. Zero keeps
    /// one class: every caller is served as it arrives.
    /// </param>
    public RateLimiter(int maxRequests = 5, TimeSpan? timeWindow = null, int backgroundReserve = 0)
    {
        // A capacity of zero or less can never release a request; fail fast at
        // construction rather than throwing InvalidOperationException from the
        // first WaitAsync (cf. SemaphoreSlim's initialCount).
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRequests);

        // The reserve must leave urgent callers a slot, or they could never be served.
        ArgumentOutOfRangeException.ThrowIfNegative(backgroundReserve);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(backgroundReserve, maxRequests);

        _maxRequests = maxRequests;
        _timeWindow = timeWindow ?? TimeSpan.FromMinutes(1);
        _requestTimes = new Queue<DateTime>();
        _backgroundReserve = backgroundReserve;
    }

    public Task WaitAsync(CancellationToken cancellationToken = default) =>
        WaitAsync(urgent: false, cancellationToken);

    // Two classes share one window. An urgent caller goes ahead of background ones, which keep
    // the reserved share while they wait, so time-critical requests are never queued behind
    // refreshes and the refreshes are never starved. Capacity either class leaves unused serves
    // the other.
    public async Task WaitAsync(bool urgent, CancellationToken cancellationToken = default)
    {
        var waiting = false;
        try
        {
            while (true)
            {
                TimeSpan waitTime;

                lock (_lock)
                {
                    var now = DateTime.UtcNow;
                    waitTime = CalculateWaitTime(now);

                    // If a 429 triggered a pause, wait for the longer of the two
                    var pauseRemaining = _pauseUntil - now;
                    if (pauseRemaining > waitTime)
                    {
                        waitTime = pauseRemaining;
                    }

                    if (waitTime <= TimeSpan.Zero)
                    {
                        var othersWaiting = urgent ? _backgroundWaiting : _urgentWaiting;
                        if (
                            MayTake(
                                urgent,
                                _urgentTimes.Count,
                                _backgroundTimes.Count,
                                othersWaiting > 0,
                                _maxRequests,
                                _backgroundReserve
                            )
                        )
                        {
                            _requestTimes.Enqueue(now);
                            (urgent ? _urgentTimes : _backgroundTimes).Enqueue(now);
                            return;
                        }
                        waitTime = YieldInterval;
                    }

                    if (!waiting)
                    {
                        waiting = true;
                        if (urgent)
                            _urgentWaiting++;
                        else
                            _backgroundWaiting++;
                    }
                }

                await Task.Delay(waitTime, cancellationToken);
            }
        }
        finally
        {
            if (waiting)
            {
                lock (_lock)
                {
                    if (urgent)
                        _urgentWaiting--;
                    else
                        _backgroundWaiting--;
                }
            }
        }
    }

    // Whether a caller may take a free slot. Pure so the sharing rule is pinnable without a clock.
    internal static bool MayTake(
        bool urgent,
        int urgentInWindow,
        int backgroundInWindow,
        bool otherClassWaiting,
        int maxRequests,
        int backgroundReserve
    ) =>
        !otherClassWaiting
        || (
            urgent
                ? urgentInWindow < maxRequests - backgroundReserve
                : backgroundInWindow < backgroundReserve
        );

    public bool IsThrottled
    {
        get
        {
            lock (_lock)
            {
                return DateTime.UtcNow < _pauseUntil;
            }
        }
    }

    public void PauseFor(TimeSpan duration)
    {
        lock (_lock)
        {
            var newPauseUntil = DateTime.UtcNow + duration;
            if (newPauseUntil > _pauseUntil)
            {
                _pauseUntil = newPauseUntil;
            }
        }
    }

    private TimeSpan CalculateWaitTime(DateTime now)
    {
        CleanupOldRequests(now);

        if (_requestTimes.Count < _maxRequests)
        {
            return TimeSpan.Zero;
        }

        var oldestRequest = _requestTimes.Peek();
        return (oldestRequest + _timeWindow) - now;
    }

    private void CleanupOldRequests(DateTime now)
    {
        foreach (var times in new[] { _requestTimes, _urgentTimes, _backgroundTimes })
        {
            while (times.Count > 0 && times.Peek() <= now - _timeWindow)
            {
                times.Dequeue();
            }
        }
    }
}
