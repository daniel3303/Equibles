using Equibles.EquityMarkets.BusinessLogic.Directory;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;
using Equibles.EquityMarkets.HostedService.Configuration;
using Equibles.EquityMarkets.Repositories;

namespace Equibles.EquityMarkets.HostedService;

// A control loop over the registration table: every catalog market with a directory adapter runs when
// the operator asks or its directory is a day old, and every outcome lands on the row the operator page reads.
public class EquityMarketDirectoryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<EquityMarketsScraperOptions> options,
    ILogger<EquityMarketDirectoryWorker> logger
) : BackgroundService
{
    // A pass that captured nothing (source down, FIRDS not loaded yet) leaves the row's refresh time alone
    // so the operator sees it is stale; this keeps such a market from being retried every control tick.
    private readonly Dictionary<string, DateTime> _lastAttempt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _consecutiveFailures = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDueMarkets(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Equity market directory control loop faulted");
            }
            try
            {
                await Task.Delay(
                    TimeSpan.FromMinutes(
                        Math.Max(1, options.Value.DirectoryControlIntervalMinutes)
                    ),
                    stoppingToken
                );
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task RunDueMarkets(CancellationToken stoppingToken)
    {
        List<EquityMarketRegistration> due;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var registrations =
                scope.ServiceProvider.GetRequiredService<EquityMarketRegistrationRepository>();
            await registrations.EnsureSeeded(EquityMarketCatalog.All, stoppingToken);
            var interval = TimeSpan.FromHours(
                Math.Max(1, options.Value.DirectoryRefreshIntervalHours)
            );
            var retry = TimeSpan.FromMinutes(
                Math.Max(1, options.Value.DirectoryRetryIntervalMinutes)
            );
            var now = DateTime.UtcNow;
            due = [];
            foreach (var market in EquityMarketCatalog.All)
            {
                if (market.DirectorySource == null)
                    continue;
                var row = await registrations.GetByCode(market.Code, stoppingToken);
                if (
                    row != null
                    && IsDue(
                        row,
                        _lastAttempt.TryGetValue(market.Code, out var attemptedAt)
                            ? attemptedAt
                            : null,
                        _consecutiveFailures.GetValueOrDefault(market.Code),
                        now,
                        interval,
                        retry
                    )
                )
                    due.Add(row);
            }
        }
        foreach (var row in due)
        {
            stoppingToken.ThrowIfCancellationRequested();
            var market = EquityMarketCatalog.TryGet(row.Code);
            await RunMarket(market, row.DirectoryRefreshRequestedAt, stoppingToken);
        }
    }

    // An operator request runs at once, but only once per press: a failed pass leaves the request standing so
    // the page still shows it outstanding, and without this the market would run every control tick for ever.
    // Otherwise a stale directory runs unless this process tried it recently.
    internal static bool IsDue(
        EquityMarketRegistration row,
        DateTime? attemptedAt,
        int consecutiveFailures,
        DateTime now,
        TimeSpan refreshInterval,
        TimeSpan retryInterval
    )
    {
        if (
            row.DirectoryRefreshRequestedAt != null
            && (attemptedAt == null || row.DirectoryRefreshRequestedAt > attemptedAt)
        )
            return true;
        var stale =
            row.DirectoryRefreshedAt == null || row.DirectoryRefreshedAt < now - refreshInterval;
        var wait = RetryWait(retryInterval, refreshInterval, consecutiveFailures);
        var recentlyTried = attemptedAt != null && attemptedAt > now - wait;
        return stale && !recentlyTried;
    }

    // The wait after a failed pass doubles with each consecutive failure up to the refresh interval: a source
    // that refuses its own data refuses it again on the next call, and a capture can cost hundreds of requests.
    // An operator request still runs at once, so nothing is stuck behind the backoff.
    internal static TimeSpan RetryWait(
        TimeSpan retryInterval,
        TimeSpan refreshInterval,
        int consecutiveFailures
    )
    {
        if (retryInterval <= TimeSpan.Zero || retryInterval >= refreshInterval)
            return refreshInterval;
        var wait = retryInterval;
        // Doubling in place stops at the cap, so no configuration can overflow the multiplication.
        for (var doubling = 1; doubling < consecutiveFailures && wait < refreshInterval; doubling++)
            wait += wait;
        return wait < refreshInterval ? wait : refreshInterval;
    }

    // A request is cleared only by a pass that served it; one raised during the pass or a failed pass keeps it.
    internal static bool RequestServed(
        DateTime? requestedBefore,
        DateTime? requestedNow,
        bool succeeded
    ) => succeeded && requestedNow == requestedBefore;

    private async Task RunMarket(
        EquityMarket market,
        DateTime? requestedAt,
        CancellationToken stoppingToken
    )
    {
        _lastAttempt[market.Code] = DateTime.UtcNow;
        EquityMarketDirectoryImportResult result;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var importer =
                scope.ServiceProvider.GetRequiredService<EquityMarketDirectoryImporter>();
            result = await importer.Import(market, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "{Market} directory refresh failed; retained identity is unchanged for unresolved source records",
                market.Code
            );
            result = new EquityMarketDirectoryImportResult { Error = exception.Message };
        }
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var registrations =
                scope.ServiceProvider.GetRequiredService<EquityMarketRegistrationRepository>();
            var row = await registrations.GetByCode(market.Code, stoppingToken);
            if (row == null)
                return;
            if (RequestServed(requestedAt, row.DirectoryRefreshRequestedAt, result.Error == null))
                row.DirectoryRefreshRequestedAt = null;
            row.LastError = result.Error is { Length: > 1000 }
                ? result.Error[..1000]
                : result.Error;
            row.UpdatedAt = DateTime.UtcNow;
            // A pass that never captured the directory leaves the previous counts and refresh time alone.
            if (result.Error == null)
            {
                row.DirectoryRefreshedAt = DateTime.UtcNow;
                row.DirectoryListingCount = result.Listings;
                row.DirectoryImportedCount = result.Imported;
                row.DirectoryCurrentCount = result.Current;
                row.DirectorySkippedCount = result.Skipped;
                row.DirectoryFailedCount = result.Failed;
            }
            await registrations.SaveChanges();
        }
        if (result.Error == null)
            _consecutiveFailures.Remove(market.Code);
        else
            _consecutiveFailures[market.Code] =
                _consecutiveFailures.GetValueOrDefault(market.Code) + 1;
    }
}
