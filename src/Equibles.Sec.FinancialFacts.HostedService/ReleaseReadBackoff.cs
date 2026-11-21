namespace Equibles.Sec.FinancialFacts.HostedService;

/// <summary>
/// When a release whose read threw is tried again: an hour later after the first failure, an
/// hour longer after each further one, at most a day. A release is never given up on, since an
/// outage fails releases that have nothing wrong with them; the state lasts for the process.
/// </summary>
public sealed class ReleaseReadBackoff
{
    private static readonly TimeSpan Step = TimeSpan.FromHours(1);
    private static readonly TimeSpan Longest = TimeSpan.FromHours(24);

    private readonly Dictionary<Guid, (int Failures, DateTime RetryAt)> _failed = [];

    public void Failed(Guid releaseId, DateTime now)
    {
        var failures = _failed.TryGetValue(releaseId, out var state) ? state.Failures + 1 : 1;
        var wait = Step * failures;
        _failed[releaseId] = (failures, now + (wait < Longest ? wait : Longest));
    }

    public void Succeeded(Guid releaseId) => _failed.Remove(releaseId);

    /// <summary>The releases whose next try is still ahead.</summary>
    public List<Guid> Waiting(DateTime now) =>
        _failed.Where(entry => entry.Value.RetryAt > now).Select(entry => entry.Key).ToList();
}
