using System.Collections.Concurrent;
using System.Linq.Expressions;
using Equibles.CommonStocks.BusinessLogic.Websites;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.HostedService.Configuration;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Messaging.Contracts.CommonStocks;
using Equibles.Worker;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Fills in <c>CommonStock.Website</c> for stocks that don't have one yet, one
/// bounded batch per cycle. Candidate URLs come from the registered
/// <see cref="IWebsiteSource"/> implementations, consulted in priority order so
/// each source only sees the stocks every more-authoritative source left
/// unfilled; the first candidate that survives a reachability probe wins.
/// Upstream of IR discovery: stocks without a website can never get an IR page,
/// news, events, or call artefacts.
/// </summary>
[Service]
public class WebsiteDiscoveryService : IImporter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReadOnlyList<IWebsiteSource> _sources;
    private readonly WebsiteProbeClient _probeClient;
    private readonly ErrorReporter _errorReporter;
    private readonly ILogger<WebsiteDiscoveryService> _logger;
    private readonly WebsiteDiscoveryOptions _options;
    private readonly IBus _bus;

    public WebsiteDiscoveryService(
        IServiceScopeFactory scopeFactory,
        IEnumerable<IWebsiteSource> sources,
        WebsiteProbeClient probeClient,
        ErrorReporter errorReporter,
        ILogger<WebsiteDiscoveryService> logger,
        IOptions<WebsiteDiscoveryOptions> options,
        IBus bus
    )
    {
        _scopeFactory = scopeFactory;
        _sources = sources.OrderBy(s => s.Priority).ToList();
        _probeClient = probeClient;
        _errorReporter = errorReporter;
        _logger = logger;
        _options = options.Value;
        _bus = bus;
    }

    public Task Import(CancellationToken cancellationToken) => DiscoverBatch(cancellationToken);

    /// <summary>
    /// Runs one discovery batch and returns true when it filled a full batch — i.e. more pending
    /// stocks remain — so the worker chains straight into the next batch (short ContinuationInterval)
    /// instead of sleeping the full SleepInterval, draining a large backlog in successive bursts
    /// rather than one batch per cycle.
    /// </summary>
    public async Task<bool> DiscoverBatch(CancellationToken cancellationToken)
    {
        if (_sources.Count == 0)
        {
            _logger.LogInformation("Website discovery: no website sources registered");
            return false;
        }

        var batch = await LoadCandidates(cancellationToken);
        if (batch.Count == 0)
        {
            _logger.LogInformation("Website discovery: no stocks pending discovery");
            return false;
        }

        _logger.LogInformation(
            "Website discovery: attempting {Count} stocks across {Sources} sources",
            batch.Count,
            _sources.Count
        );

        var remaining = batch;
        var discovered = 0;
        var anySourceFailed = false;
        foreach (var source in _sources)
        {
            if (remaining.Count == 0)
                break;

            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyDictionary<Guid, string> candidates;
            try
            {
                candidates = await source.FindWebsites(remaining, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A failing source must not block the less-authoritative ones —
                // its stocks simply fall through, and the batch skips the
                // definitive-miss stamp so they retry next cycle.
                anySourceFailed = true;
                _logger.LogError(ex, "Website source {Source} failed", source.Name);
                await _errorReporter.Report(
                    ErrorSource.WebsiteDiscovery,
                    $"FindWebsites({source.Name})",
                    ex.Message,
                    ex.StackTrace
                );
                continue;
            }

            // Probe candidates concurrently: each probe is a stealth-browser render that can take up
            // to the render timeout for a dead/slow host, so a serial loop made a batch with many
            // dead candidates take many minutes (and the cycle never reached its commit step). The
            // stealth client caps actual renders via its own semaphore; Persist/MarkChecked each open
            // their own scope, so this is safe to fan out. Stocks with no candidate skip the probe.
            var unfilled = new ConcurrentBag<WebsiteSourceStock>();
            await Parallel.ForEachAsync(
                remaining,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, _options.ProbeConcurrency),
                    CancellationToken = cancellationToken,
                },
                async (stock, ct) =>
                {
                    var validated = candidates.TryGetValue(stock.Id, out var candidate)
                        ? await _probeClient.Validate(candidate, ct)
                        : null;
                    if (validated != null && await Persist(stock.Id, validated))
                    {
                        _logger.LogDebug(
                            "Discovered website for {Ticker} via {Source}: {Url}",
                            stock.Ticker,
                            source.Name,
                            validated
                        );
                    }
                    else
                    {
                        unfilled.Add(stock);
                    }
                }
            );

            discovered += remaining.Count - unfilled.Count;
            remaining = unfilled.ToList();
        }

        // Every source definitively missed these stocks — stamp the attempt so they
        // back off for the cooldown window instead of re-occupying a batch slot every
        // cycle. Skipped when a source errored: those stocks deserve a clean retry.
        if (!anySourceFailed)
        {
            foreach (var stock in remaining)
                await MarkChecked(stock.Id);
        }

        _logger.LogInformation(
            "Website discovery complete. Discovered {Discovered} websites, {Missed} missed",
            discovered,
            remaining.Count
        );

        // A full batch means more pending stocks remain (the candidate query was capped by BatchSize),
        // so signal the worker to chain into the next batch rather than sleeping the full interval.
        return batch.Count >= _options.BatchSize;
    }

    private async Task<List<WebsiteSourceStock>> LoadCandidates(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        var cutoff = DateTime.UtcNow.AddDays(-_options.CheckCooldownDays);
        // Largest companies first: a single bad bulk run can leave thousands of stocks pending,
        // and alphabetical order buries the high-value ones (e.g. TSLA, WMT) behind obscure
        // tickers for hours. Market cap is the priority signal; unknown caps (0) drain last,
        // tie-broken alphabetically for a stable order.
        var rows = await repo.GetAll()
            .Where(PendingDiscovery(cutoff))
            .OrderByDescending(s => s.MarketCapitalization)
            .ThenBy(s => s.Ticker)
            .Take(_options.BatchSize)
            .Select(s => new
            {
                s.Id,
                s.Ticker,
                s.Cik,
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new WebsiteSourceStock(r.Id, r.Ticker, r.Cik)).ToList();
    }

    private async Task<bool> Persist(Guid commonStockId, string website)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        var stock = await repo.Get(commonStockId);
        // The stock may have been deleted, or filled in by an overlapping run,
        // between loading the batch and persisting — leave an existing value alone.
        if (stock == null || !string.IsNullOrEmpty(stock.Website))
            return false;

        stock.Website = website;
        stock.WebsiteCheckedAt = DateTime.UtcNow;
        await repo.SaveChanges();

        // The website is the input IR discovery needs, so cascade straight into an IR probe instead
        // of waiting out that worker's own cooldown. Published via the root bus after the write
        // commits (financial-domain event, bypasses any bus outbox); the consumer is idempotent and
        // a reconciliation backstop in IR discovery's candidate query catches a publish lost to a
        // crash, so at-least-once delivery is safe.
        await _bus.Publish(new StockWebsiteDiscovered(stock.Id, stock.Ticker, website));
        return true;
    }

    private async Task MarkChecked(Guid commonStockId)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        var stock = await repo.Get(commonStockId);
        if (stock == null)
            return;

        stock.WebsiteCheckedAt = DateTime.UtcNow;
        await repo.SaveChanges();
    }

    /// <summary>
    /// Stocks eligible for a website discovery attempt: no website yet (empty string
    /// counts as missing — SEC metadata blanks were historically stored as "") and no
    /// attempt since <paramref name="cutoff"/> (never-attempted stocks are always
    /// eligible).
    /// </summary>
    public static Expression<Func<CommonStock, bool>> PendingDiscovery(DateTime cutoff)
    {
        return s =>
            (s.Website == null || s.Website == "")
            && (s.WebsiteCheckedAt == null || s.WebsiteCheckedAt < cutoff);
    }
}
