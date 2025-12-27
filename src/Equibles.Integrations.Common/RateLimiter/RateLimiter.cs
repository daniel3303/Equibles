namespace Equibles.Integrations.Common.RateLimiter;

public class RateLimiter : IRateLimiter {
    private readonly int _maxRequests;
    private readonly TimeSpan _timeWindow;
    private readonly Queue<DateTime> _requestTimes;
    private readonly Lock _lock = new();
    private DateTime _pauseUntil = DateTime.MinValue;

    public RateLimiter(int maxRequests = 5, TimeSpan? timeWindow = null) {
        _maxRequests = maxRequests;
        _timeWindow = timeWindow ?? TimeSpan.FromMinutes(1);
        _requestTimes = new Queue<DateTime>();
    }

    public async Task WaitAsync() {
        TimeSpan waitTime;

        lock (_lock) {
            waitTime = CalculateWaitTime();

            // If a 429 triggered a pause, wait for the longer of the two
            var pauseRemaining = _pauseUntil - DateTime.UtcNow;
            if (pauseRemaining > waitTime) {
                waitTime = pauseRemaining;
            }

            if (waitTime <= TimeSpan.Zero) {
                _requestTimes.Enqueue(DateTime.UtcNow);
            }
        }

        if (waitTime > TimeSpan.Zero) {
            await Task.Delay(waitTime);
            await WaitAsync();
        }
    }

    public void PauseFor(TimeSpan duration) {
        lock (_lock) {
            var newPauseUntil = DateTime.UtcNow + duration;
            if (newPauseUntil > _pauseUntil) {
                _pauseUntil = newPauseUntil;
            }
        }
    }

    private TimeSpan CalculateWaitTime() {
        var now = DateTime.UtcNow;
        CleanupOldRequests(now);

        if (_requestTimes.Count < _maxRequests) {
            return TimeSpan.Zero;
        }

        var oldestRequest = _requestTimes.Peek();
        return (oldestRequest + _timeWindow) - now;
    }

    private void CleanupOldRequests(DateTime now) {
        while (_requestTimes.Count > 0 && _requestTimes.Peek() < now - _timeWindow) {
            _requestTimes.Dequeue();
        }
    }
}