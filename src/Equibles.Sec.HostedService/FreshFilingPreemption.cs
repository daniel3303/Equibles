using Equibles.CommonStocks.Data.Models;

namespace Equibles.Sec.HostedService;

/// <summary>
/// Orders one scraping cycle so no backlog filing is read while a flagged filing waits. A full pass reads every
/// unread filing and polls the feed between them; each company the poll flags gets a fresh pass, which reads only
/// recent filings and never polls. A company a fresh pass left older filings for gets its full pass later, between
/// companies. Running whole passes inside one another starved early-flagged filings behind fund-trust backlogs.
/// </summary>
internal sealed class FreshFilingPreemption
{
    private readonly Func<Task<List<EquityIssuer>>> _pollFresh;
    private readonly Func<EquityIssuer, Task> _processFull;
    private readonly Func<EquityIssuer, Task<bool>> _processFresh;
    private readonly CancellationToken _cancellationToken;
    private readonly HashSet<Guid> _processed = [];
    private readonly List<EquityIssuer> _awaitingFullPass = [];
    private Guid? _fullPassInFlight;
    private bool _flaggedDuringFullPass;
    private bool _freshPassInFlight;

    /// <param name="processFresh">Reads only recent filings; true when it left older filings unread.</param>
    public FreshFilingPreemption(
        Func<Task<List<EquityIssuer>>> pollFresh,
        Func<EquityIssuer, Task> processFull,
        Func<EquityIssuer, Task<bool>> processFresh,
        CancellationToken cancellationToken
    )
    {
        _pollFresh = pollFresh;
        _processFull = processFull;
        _processFresh = processFresh;
        _cancellationToken = cancellationToken;
    }

    public bool WasProcessed(EquityIssuer company) => _processed.Contains(company.Id);

    public async Task Process(EquityIssuer company)
    {
        _processed.Add(company.Id);
        _awaitingFullPass.RemoveAll(c => c.Id == company.Id);
        _fullPassInFlight = company.Id;
        _flaggedDuringFullPass = false;
        try
        {
            await _processFull(company);
        }
        finally
        {
            _fullPassInFlight = null;
        }

        // Its enumeration was read before this flag, so the flagged filing is not in it.
        if (_flaggedDuringFullPass && !_cancellationToken.IsCancellationRequested)
            await ProcessFresh(company);
    }

    public async Task ProcessFresh(EquityIssuer company)
    {
        _freshPassInFlight = true;
        bool leftOlderFilings;
        try
        {
            leftOlderFilings = await _processFresh(company);
        }
        finally
        {
            _freshPassInFlight = false;
        }

        if (!leftOlderFilings)
            _processed.Add(company.Id);
        else if (!_processed.Contains(company.Id) && _awaitingFullPass.All(c => c.Id != company.Id))
            _awaitingFullPass.Add(company);
    }

    /// <summary>
    /// Gives every company the feed flags now a fresh pass. Inside a fresh pass it does nothing, so a flagged
    /// filing never waits behind another company's reading.
    /// </summary>
    public async Task RunFresh()
    {
        if (_cancellationToken.IsCancellationRequested || _freshPassInFlight)
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
            if (company.Id == _fullPassInFlight)
            {
                _flaggedDuringFullPass = true;
                continue;
            }
            await ProcessFresh(company);
        }
    }

    public async Task DrainFullPasses()
    {
        while (_awaitingFullPass.Count > 0 && !_cancellationToken.IsCancellationRequested)
        {
            await RunFresh();
            if (_awaitingFullPass.Count == 0)
                return;
            await Process(_awaitingFullPass[0]);
        }
    }
}
