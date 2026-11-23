using Equibles.CommonStocks.Data.Models;

namespace Equibles.Sec.HostedService;

/// <summary>
/// Runs companies the real-time feed flags ahead of the sweep in progress, polled between companies and
/// between one company's filings: a fund trust's first sweep can read hours of reports, and every new
/// filing on EDGAR waited behind it.
/// </summary>
internal sealed class FreshFilingPreemption
{
    private readonly Func<Task<List<EquityIssuer>>> _pollFresh;
    private readonly Func<EquityIssuer, Task> _process;
    private readonly CancellationToken _cancellationToken;
    private readonly HashSet<Guid> _processed = [];
    private readonly List<Guid> _inFlight = [];
    private readonly Dictionary<Guid, EquityIssuer> _flaggedWhileInFlight = [];

    public FreshFilingPreemption(
        Func<Task<List<EquityIssuer>>> pollFresh,
        Func<EquityIssuer, Task> process,
        CancellationToken cancellationToken
    )
    {
        _pollFresh = pollFresh;
        _process = process;
        _cancellationToken = cancellationToken;
    }

    public bool WasProcessed(EquityIssuer company) => _processed.Contains(company.Id);

    public async Task Process(EquityIssuer company)
    {
        _processed.Add(company.Id);
        _inFlight.Add(company.Id);
        try
        {
            await _process(company);
        }
        finally
        {
            _inFlight.RemoveAt(_inFlight.Count - 1);
        }

        // Its enumeration was read before this flag, so the flagged filing is not in it.
        if (
            _flaggedWhileInFlight.Remove(company.Id, out var flagged)
            && !_cancellationToken.IsCancellationRequested
        )
            await Process(flagged);
    }

    /// <summary>
    /// Processes every company the feed flags now. A company already in flight is never nested inside
    /// itself; it runs again once its current pass ends.
    /// </summary>
    public async Task RunFresh()
    {
        if (_cancellationToken.IsCancellationRequested)
            return;

        // Any poll failure during shutdown must not escape into the filing loop: its per-type catch would skip
        // the rest of the company's filings, and the company would still be stamped as synced.
        List<EquityIssuer> fresh;
        try
        {
            fresh = await _pollFresh();
        }
        catch (Exception) when (_cancellationToken.IsCancellationRequested)
        {
            return;
        }

        foreach (var company in fresh)
        {
            if (_cancellationToken.IsCancellationRequested)
                return;
            if (_inFlight.Contains(company.Id))
            {
                _flaggedWhileInFlight[company.Id] = company;
                continue;
            }
            await Process(company);
        }
    }
}
