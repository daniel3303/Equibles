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
/// Fills in <c>EquityIssuer.Website</c> for issuers of the current directory, US and verified
/// venue listings alike, that don't have one yet, one bounded batch per cycle. Candidate URLs
/// come from the registered <see cref="IWebsiteSource"/> implementations, consulted in priority
/// order so each source only sees the issuers every more-authoritative source left unfilled;
/// the first candidate that survives a reachability probe wins. A stored website judged under
/// older rules is re-checked once (<see cref="WebsiteDiscoveryVersion"/>). Upstream of IR discovery:
/// issuers without a website can never get an IR page, news, events, or call artefacts.
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

    private static readonly TimeSpan RecheckProbeRetryDelay = TimeSpan.FromDays(1);

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
    /// Runs one discovery batch and one re-check batch, and returns true when either filled a full
    /// batch — i.e. more stocks remain — so the worker chains straight into the next batch (short
    /// ContinuationInterval) instead of sleeping the full SleepInterval, draining a large backlog in
    /// successive bursts rather than one batch per cycle.
    /// </summary>
    public async Task<bool> DiscoverBatch(CancellationToken cancellationToken)
    {
        if (_sources.Count == 0)
        {
            _logger.LogInformation("Website discovery: no website sources registered");
            return false;
        }

        var morePending = await DiscoverPending(cancellationToken);
        var moreToRecheck = await RecheckBatch(cancellationToken);
        return morePending || moreToRecheck;
    }

    private async Task<bool> DiscoverPending(CancellationToken cancellationToken)
    {
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
            catch (WebsiteSourceUnavailableException ex)
            {
                // A throttled or overloaded backend is an expected outage, not a fault:
                // its stocks fall through and retry next cycle without an Errors row.
                anySourceFailed = true;
                _logger.LogWarning(
                    "Website source {Source} unavailable this batch: {Reason}",
                    source.Name,
                    ex.Message
                );
                continue;
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
                    ex
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
                            "Discovered website for {Symbol} via {Source}: {Url}",
                            stock.Symbol,
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

    /// <summary>
    /// Re-checks one batch of stored websites judged under older rules: each source in priority
    /// order may name a replacement, the first that survives the probe overwrites the website and
    /// cascades into IR discovery, and the rest are stamped with the current version so each is
    /// re-checked once. Returns true when a full batch ran cleanly, so more remain.
    /// </summary>
    private async Task<bool> RecheckBatch(CancellationToken cancellationToken)
    {
        var batch = await LoadRecheckCandidates(cancellationToken);
        if (batch.Count == 0)
            return false;

        var remaining = batch;
        var anySourceFailed = false;
        var deferred = new HashSet<Guid>();
        foreach (var source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyDictionary<Guid, string> replacements;
            try
            {
                replacements = await source.FindReplacements(remaining, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (WebsiteSourceUnavailableException ex)
            {
                anySourceFailed = true;
                _logger.LogWarning(
                    "Website source {Source} unavailable for the re-check: {Reason}",
                    source.Name,
                    ex.Message
                );
                continue;
            }
            catch (Exception ex)
            {
                anySourceFailed = true;
                _logger.LogError(ex, "Website source {Source} failed the re-check", source.Name);
                await _errorReporter.Report(
                    ErrorSource.WebsiteDiscovery,
                    $"FindReplacements({source.Name})",
                    ex
                );
                continue;
            }

            // Replacements are rare, so their probes run one at a time.
            var kept = new List<WebsiteSourceStock>();
            foreach (var stock in remaining)
            {
                if (replacements.TryGetValue(stock.Id, out var replacement))
                {
                    var validated = await _probeClient.Validate(replacement, cancellationToken);
                    // A deferred or settled stock leaves the batch either way, so no later
                    // source probes it again this cycle.
                    if (validated == null)
                    {
                        if (await DeferAfterFailedProbe(stock.Id))
                            deferred.Add(stock.Id);
                        continue;
                    }
                    if (await Replace(stock, validated, source.Name))
                        continue;
                }
                kept.Add(stock);
            }
            remaining = kept;
        }

        // A stock left unstamped after a source error is re-checked next cycle.
        if (!anySourceFailed)
        {
            foreach (var stock in remaining)
                await StampVersion(stock.Id);
        }

        _logger.LogInformation(
            "Website re-check complete. {Settled} of {Count} stored websites settled, {Deferred} deferred after a failed replacement probe",
            batch.Count - deferred.Count,
            batch.Count,
            deferred.Count
        );
        // Chaining needs progress: a batch held entirely by deferred stocks waits out the sleep.
        return !anySourceFailed
            && batch.Count >= _options.BatchSize
            && deferred.Count < batch.Count;
    }

    private async Task<List<WebsiteSourceStock>> LoadRecheckCandidates(
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        EquityIssuerRepository repo =
            scope.ServiceProvider.GetRequiredService<EquityIssuerRepository>();

        var rows = await repo.GetCurrentDirectory()
            .Where(
                PendingRecheck(
                    WebsiteDiscoveryVersion.Current,
                    DateTime.UtcNow - RecheckProbeRetryDelay
                )
            )
            .OrderByDescending(s => s.Presentation.Listing.Security.MarketCapitalization)
            .ThenBy(s => s.Presentation.Listing.MarketIdentifierCode ?? "")
            .ThenBy(s => s.Presentation.Listing.Ticker)
            .Take(_options.BatchSize)
            .Select(s => new
            {
                s.Id,
                Ticker = s.Presentation.Listing.Ticker,
                s.Cik,
                s.LegalEntityIdentifier,
                s.Presentation.Listing.Security.Isin,
                s.Presentation.Listing.MarketIdentifierCode,
                s.Presentation.Listing.MarketCountryCode,
                s.Website,
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new WebsiteSourceStock(
                r.Id,
                r.Ticker,
                r.Cik,
                r.LegalEntityIdentifier,
                r.Isin,
                r.MarketIdentifierCode,
                r.MarketCountryCode,
                r.Website
            ))
            .ToList();
    }

    private async Task<bool> Replace(WebsiteSourceStock stock, string website, string sourceName)
    {
        using var scope = _scopeFactory.CreateScope();
        EquityIssuerRepository repo =
            scope.ServiceProvider.GetRequiredService<EquityIssuerRepository>();

        EquityIssuer issuer = await repo.GetCurrentDirectoryIssuer(stock.Id);
        // Another writer may have changed the website since the batch loaded; only the website
        // the source re-judged is replaced.
        if (issuer == null || issuer.Website != stock.Website)
            return false;

        issuer.Website = website;
        issuer.WebsiteCheckedAt = DateTime.UtcNow;
        issuer.WebsiteDiscoveryVersion = WebsiteDiscoveryVersion.Current;
        issuer.WebsiteRecheckProbeFailedAt = null;
        await repo.SaveChanges();
        _logger.LogInformation(
            "Website re-check replaced {Old} with {New} for {Symbol} via {Source}",
            stock.Website,
            website,
            stock.Symbol,
            sourceName
        );

        await PublishDiscovered(issuer, website);
        return true;
    }

    private async Task StampVersion(Guid commonStockId)
    {
        using var scope = _scopeFactory.CreateScope();
        EquityIssuerRepository repo =
            scope.ServiceProvider.GetRequiredService<EquityIssuerRepository>();

        EquityIssuer stock = await repo.GetCurrentDirectoryIssuer(commonStockId);
        if (stock == null)
            return;

        stock.WebsiteDiscoveryVersion = WebsiteDiscoveryVersion.Current;
        stock.WebsiteRecheckProbeFailedAt = null;
        await repo.SaveChanges();
    }

    // A probe that could not run (a sidecar restart) reads the same as a dead site, so a first
    // failure defers the stock a day and a second one settles it. True when deferred.
    private async Task<bool> DeferAfterFailedProbe(Guid commonStockId)
    {
        using var scope = _scopeFactory.CreateScope();
        EquityIssuerRepository repo =
            scope.ServiceProvider.GetRequiredService<EquityIssuerRepository>();

        EquityIssuer stock = await repo.GetCurrentDirectoryIssuer(commonStockId);
        if (stock == null)
            return false;

        var firstFailure = stock.WebsiteRecheckProbeFailedAt == null;
        if (firstFailure)
            stock.WebsiteRecheckProbeFailedAt = DateTime.UtcNow;
        else
        {
            stock.WebsiteDiscoveryVersion = WebsiteDiscoveryVersion.Current;
            stock.WebsiteRecheckProbeFailedAt = null;
        }
        await repo.SaveChanges();
        return firstFailure;
    }

    private async Task<List<WebsiteSourceStock>> LoadCandidates(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        EquityIssuerRepository repo =
            scope.ServiceProvider.GetRequiredService<EquityIssuerRepository>();

        var cutoff = DateTime.UtcNow.AddDays(-_options.CheckCooldownDays);
        // Largest companies first: a single bad bulk run can leave thousands of issuers pending,
        // and alphabetical order buries the high-value ones (e.g. TSLA, WMT) behind obscure
        // tickers for hours. Market cap is the priority signal; unknown caps (0) drain last,
        // tie-broken by venue then ticker for a stable order across markets.
        var rows = await repo.GetCurrentDirectory()
            .Where(PendingDiscovery(cutoff))
            .OrderByDescending(s => s.Presentation.Listing.Security.MarketCapitalization)
            .ThenBy(s => s.Presentation.Listing.MarketIdentifierCode ?? "")
            .ThenBy(s => s.Presentation.Listing.Ticker)
            .Take(_options.BatchSize)
            .Select(s => new
            {
                s.Id,
                Ticker = s.Presentation.Listing.Ticker,
                s.Cik,
                s.LegalEntityIdentifier,
                s.Presentation.Listing.Security.Isin,
                s.Presentation.Listing.MarketIdentifierCode,
                s.Presentation.Listing.MarketCountryCode,
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new WebsiteSourceStock(
                r.Id,
                r.Ticker,
                r.Cik,
                r.LegalEntityIdentifier,
                r.Isin,
                r.MarketIdentifierCode,
                r.MarketCountryCode
            ))
            .ToList();
    }

    private async Task<bool> Persist(Guid commonStockId, string website)
    {
        using var scope = _scopeFactory.CreateScope();
        EquityIssuerRepository repo =
            scope.ServiceProvider.GetRequiredService<EquityIssuerRepository>();

        EquityIssuer stock = await repo.GetCurrentDirectoryIssuer(commonStockId);
        // The issuer may have been deleted, or filled in by an overlapping run,
        // between loading the batch and persisting — leave an existing value alone.
        if (stock == null || !string.IsNullOrEmpty(stock.Website))
            return false;

        stock.Website = website;
        stock.WebsiteCheckedAt = DateTime.UtcNow;
        stock.WebsiteDiscoveryVersion = WebsiteDiscoveryVersion.Current;
        stock.WebsiteRecheckProbeFailedAt = null;
        await repo.SaveChanges();

        await PublishDiscovered(stock, website);
        return true;
    }

    private async Task PublishDiscovered(EquityIssuer stock, string website)
    {
        // The website is the input IR discovery needs, so cascade straight into an IR probe instead
        // of waiting out that worker's own cooldown. Published via the root bus after the write
        // commits (financial-domain event, bypasses any bus outbox); the consumer is idempotent and
        // a reconciliation backstop in IR discovery's candidate query catches a publish lost to a
        // crash, so at-least-once delivery is safe.
        EquityListing listing = stock.Presentation.Listing;
        await _bus.Publish(
            new StockWebsiteDiscovered(
                stock.Id,
                listing.Ticker,
                website,
                listing.MarketCountryCode == "US" ? null : listing.MarketIdentifierCode
            )
        );
    }

    private async Task MarkChecked(Guid commonStockId)
    {
        using var scope = _scopeFactory.CreateScope();
        EquityIssuerRepository repo =
            scope.ServiceProvider.GetRequiredService<EquityIssuerRepository>();

        EquityIssuer stock = await repo.GetCurrentDirectoryIssuer(commonStockId);
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
    public static Expression<Func<EquityIssuer, bool>> PendingDiscovery(DateTime cutoff)
    {
        return s =>
            (s.Website == null || s.Website == "")
            && (s.WebsiteCheckedAt == null || s.WebsiteCheckedAt < cutoff);
    }

    /// <summary>
    /// Stocks whose stored website was judged under older discovery rules than
    /// <paramref name="currentVersion"/>, so their sources re-check it; one deferred after a
    /// failed replacement probe waits until <paramref name="probeFailedBefore"/>.
    /// </summary>
    public static Expression<Func<EquityIssuer, bool>> PendingRecheck(
        int currentVersion,
        DateTime probeFailedBefore
    )
    {
        return s =>
            s.Website != null
            && s.Website != ""
            && s.WebsiteDiscoveryVersion < currentVersion
            && (
                s.WebsiteRecheckProbeFailedAt == null
                || s.WebsiteRecheckProbeFailedAt < probeFailedBefore
            );
    }
}
