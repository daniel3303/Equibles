using System.Linq.Expressions;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.HostedService.Configuration;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Fills in <c>CommonStock.InvestorRelationsUrl</c> for stocks that have a known
/// website but no discovered IR page yet, one bounded batch per cycle. Probes
/// candidates concurrently and chains successive full batches so a large backlog
/// drains in bursts rather than one batch per cycle.
/// </summary>
[Service]
public class InvestorRelationsDiscoveryService : IImporter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly InvestorRelationsProbeClient _probeClient;
    private readonly ErrorReporter _errorReporter;
    private readonly ILogger<InvestorRelationsDiscoveryService> _logger;
    private readonly InvestorRelationsDiscoveryOptions _options;

    public InvestorRelationsDiscoveryService(
        IServiceScopeFactory scopeFactory,
        InvestorRelationsProbeClient probeClient,
        ErrorReporter errorReporter,
        ILogger<InvestorRelationsDiscoveryService> logger,
        IOptions<InvestorRelationsDiscoveryOptions> options
    )
    {
        _scopeFactory = scopeFactory;
        _probeClient = probeClient;
        _errorReporter = errorReporter;
        _logger = logger;
        _options = options.Value;
    }

    public Task Import(CancellationToken cancellationToken) => DiscoverBatch(cancellationToken);

    /// <summary>
    /// Runs one discovery batch and returns true when it filled a full batch — i.e. more pending
    /// stocks remain — so the worker chains into the next batch instead of sleeping the full
    /// interval, draining a large backlog in successive bursts.
    /// </summary>
    public async Task<bool> DiscoverBatch(CancellationToken cancellationToken)
    {
        var batch = await LoadCandidates(cancellationToken);
        if (batch.Count == 0)
        {
            _logger.LogInformation(
                "Investor relations discovery: no stocks with a website pending discovery"
            );
            return false;
        }

        _logger.LogInformation("Investor relations discovery: probing {Count} stocks", batch.Count);

        // Probe candidates concurrently: each probe escalates to a stealth-browser render for
        // bot-walled hosts, which can take up to the render timeout, so a serial loop made a batch
        // with many such hosts take many minutes. The stealth client caps actual renders via its own
        // semaphore and a shared rate limiter, and Persist/MarkChecked each open their own scope, so
        // this is safe to fan out.
        var discovered = 0;
        await Parallel.ForEachAsync(
            batch,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, _options.ProbeConcurrency),
                CancellationToken = cancellationToken,
            },
            async (candidate, ct) =>
            {
                if (await ProbeAndPersist(candidate, ct))
                    Interlocked.Increment(ref discovered);
            }
        );

        _logger.LogInformation(
            "Investor relations discovery complete. Discovered {Count} new IR pages",
            discovered
        );

        // A full batch means more pending stocks remain (the query was capped by BatchSize), so
        // signal the worker to chain into the next batch rather than sleeping the full interval.
        return batch.Count >= _options.BatchSize;
    }

    /// <summary>
    /// Probes one stock for an IR page now, off its current website — the immediate path the
    /// <c>StockWebsiteDiscovered</c> consumer takes when website discovery has just filled a website,
    /// so a new website cascades straight into an IR probe instead of waiting for the next batch.
    /// No-op when the stock has no website yet or already has an IR page. Idempotent.
    /// </summary>
    public async Task<bool> DiscoverForStock(
        Guid commonStockId,
        CancellationToken cancellationToken
    )
    {
        var candidate = await LoadCandidate(commonStockId, cancellationToken);
        if (candidate == null)
            return false;

        return await ProbeAndPersist(candidate, cancellationToken);
    }

    private async Task<bool> ProbeAndPersist(
        CandidateStock candidate,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var result = await _probeClient.Discover(
                candidate.Website,
                _options.CandidatePaths,
                _options.CandidateSubdomains,
                cancellationToken
            );
            // Definitive miss — every candidate was probed and none validated. Stamp the attempt so
            // the stock backs off for the cooldown window instead of re-occupying a batch slot every
            // cycle. Transient errors below deliberately skip the stamp.
            if (result == null)
            {
                await MarkChecked(candidate.Id);
                return false;
            }

            if (await Persist(candidate.Id, result))
            {
                _logger.LogDebug(
                    "Discovered investor relations page for {Ticker}: {Url} ({Platform})",
                    candidate.Ticker,
                    result.Url,
                    result.Platform
                );
                return true;
            }

            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error discovering investor relations page for {Ticker}",
                candidate.Ticker
            );
            await _errorReporter.Report(
                ErrorSource.InvestorRelationsDiscovery,
                $"Discover({candidate.Ticker})",
                ex.Message,
                ex.StackTrace
            );
            return false;
        }
    }

    private async Task<List<CandidateStock>> LoadCandidates(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        var cutoff = DateTime.UtcNow.AddDays(-_options.ProbeCooldownDays);
        // Largest companies first: high-value names (TSLA, WMT) shouldn't wait behind thousands of
        // obscure tickers. Market cap is the priority signal; unknown caps (0) drain last,
        // tie-broken alphabetically for a stable order.
        var rows = await repo.GetAll()
            .Where(PendingDiscovery(cutoff, InvestorRelationsDiscoveryVersion.Current))
            .OrderByDescending(s => s.MarketCapitalization)
            .ThenBy(s => s.Ticker)
            .Take(_options.BatchSize)
            .Select(s => new
            {
                s.Id,
                s.Ticker,
                s.Website,
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new CandidateStock(r.Id, r.Ticker, r.Website)).ToList();
    }

    private async Task<CandidateStock> LoadCandidate(
        Guid commonStockId,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        var stock = await repo.Get(commonStockId);
        // Skip when the stock has no website to probe or already has an IR page. A redelivered event
        // for an already-resolved stock no-ops here; one for a stock still missing an IR page re-runs
        // the probe (harmless — no duplicate rows, no loop, idempotent DB effect).
        if (
            stock == null
            || string.IsNullOrEmpty(stock.Website)
            || stock.InvestorRelationsUrl != null
        )
            return null;

        return new CandidateStock(stock.Id, stock.Ticker, stock.Website);
    }

    private async Task<bool> Persist(Guid commonStockId, IrDiscoveryResult result)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        var stock = await repo.Get(commonStockId);
        // The stock may have been deleted, or filled in by an overlapping run,
        // between loading the batch and persisting — leave an existing value alone.
        if (stock == null || stock.InvestorRelationsUrl != null)
            return false;

        stock.InvestorRelationsUrl = result.Url;
        stock.IrPlatformType = result.Platform;
        stock.InvestorRelationsCheckedAt = DateTime.UtcNow;
        stock.InvestorRelationsDiscoveryVersion = InvestorRelationsDiscoveryVersion.Current;
        await repo.SaveChanges();
        return true;
    }

    private async Task MarkChecked(Guid commonStockId)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        var stock = await repo.Get(commonStockId);
        if (stock == null)
            return;

        stock.InvestorRelationsCheckedAt = DateTime.UtcNow;
        stock.InvestorRelationsDiscoveryVersion = InvestorRelationsDiscoveryVersion.Current;
        await repo.SaveChanges();
    }

    /// <summary>
    /// Stocks eligible for an IR discovery probe: a known website, no discovered IR page yet, and one
    /// of — never probed; probed before the cooldown (periodic recheck for an externally-added page);
    /// probed under an older discovery version (<paramref name="currentVersion"/> — our probe improved,
    /// re-sweep the backlog now); or probed before the website was found
    /// (<c>InvestorRelationsCheckedAt &lt; WebsiteCheckedAt</c> — the input changed; the reconciliation
    /// backstop for a website-discovered event lost to a crash).
    /// </summary>
    public static Expression<Func<CommonStock, bool>> PendingDiscovery(
        DateTime cutoff,
        int currentVersion
    )
    {
        return s =>
            s.Website != null
            && s.Website != ""
            && s.InvestorRelationsUrl == null
            && (
                s.InvestorRelationsCheckedAt == null
                || s.InvestorRelationsCheckedAt < cutoff
                || s.InvestorRelationsDiscoveryVersion < currentVersion
                || s.InvestorRelationsCheckedAt < s.WebsiteCheckedAt
            );
    }

    private sealed record CandidateStock(Guid Id, string Ticker, string Website);
}
