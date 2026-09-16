using Equibles.DelayedTrades.BusinessLogic.Configuration;
using Equibles.DelayedTrades.BusinessLogic.Import;
using Equibles.DelayedTrades.BusinessLogic.Schedule;
using Equibles.DelayedTrades.Repositories;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.DelayedTrades;

namespace Equibles.DelayedTrades.HostedService;

// A quiet control loop: every catalog market with a delayed-trades source is polled inside its own session,
// settled after midnight local and re-checked once a day; every outcome lands on the market's registration row.
public class DelayedTradeScraperWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DelayedTradeScraperOptions> options,
    ErrorReporter errorReporter,
    ILogger<DelayedTradeScraperWorker> logger
) : BackgroundService
{
    private readonly Dictionary<string, DelayedTradeMarketState> _states = new(
        StringComparer.Ordinal
    );
    private DateOnly? _prunedOn;
    private DateOnly? _seededOn;

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
                logger.LogError(exception, "Delayed trades control loop faulted");
                await errorReporter.Report(
                    ErrorSource.DelayedTradeScraper,
                    "DelayedTradeScraperWorker.RunDueMarkets",
                    exception
                );
            }
            try
            {
                await Task.Delay(
                    TimeSpan.FromMinutes(Math.Max(1, options.Value.ControlIntervalMinutes)),
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
        var utcNow = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(utcNow);
        if (_seededOn != today)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope
                .ServiceProvider.GetRequiredService<EquityMarketRegistrationRepository>()
                .EnsureSeeded(EquityMarketCatalog.All, stoppingToken);
            _seededOn = today;
        }
        foreach (var market in EquityMarketCatalog.All)
        {
            stoppingToken.ThrowIfCancellationRequested();
            if (market.DelayedTradeSource == null || market.DelayedTradeLocationCode == null)
                continue;
            var state = State(market.Code);
            var plan = DelayedTradeSchedule.Plan(
                market,
                DelayedTradeClock.Zone(market),
                utcNow,
                options.Value,
                state
            );
            if (plan.IsEmpty)
                continue;
            await RunMarket(market, plan, state, utcNow, stoppingToken);
        }
        if (_prunedOn != today)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope
                .ServiceProvider.GetRequiredService<DelayedTradeImportService>()
                .PruneCaptures(utcNow, stoppingToken);
            _prunedOn = today;
        }
    }

    private async Task RunMarket(
        EquityMarket market,
        DelayedTradePlan plan,
        DelayedTradeMarketState state,
        DateTime utcNow,
        CancellationToken stoppingToken
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var source = scope
            .ServiceProvider.GetServices<IDelayedTradeSource>()
            .FirstOrDefault(candidate => candidate.SourceKey == market.DelayedTradeSource);
        if (source == null)
        {
            logger.LogWarning(
                "{Market} names delayed-trade source {Source}, which is not registered",
                market.Code,
                market.DelayedTradeSource
            );
            return;
        }
        var importer = scope.ServiceProvider.GetRequiredService<DelayedTradeImportService>();
        string error = null;
        DateTime? capturedAt = null;
        DateOnly? settledDate = null;

        if (plan.Settle || plan.Recheck)
        {
            var rederive = plan.Recheck && !plan.Settle;
            state.LastSettleAttemptUtc = utcNow;
            if (plan.Recheck)
                state.LastRecheckDate = DelayedTradeClock.LocalDate(
                    utcNow,
                    DelayedTradeClock.Zone(market)
                );
            try
            {
                var result = await importer.SettleSession(
                    market,
                    source,
                    rederive,
                    utcNow,
                    stoppingToken
                );
                if (result.Outcome != DelayedTradeSettleOutcome.NoSession)
                    capturedAt = utcNow;
                switch (result.Outcome)
                {
                    case DelayedTradeSettleOutcome.Imported:
                    case DelayedTradeSettleOutcome.Rederived:
                    case DelayedTradeSettleOutcome.AlreadyImported:
                        // An older session than the target is a holiday unless the intraday polls saw the target's
                        // prints, in which case the venue has not flipped its file yet and the hourly settle stays alive.
                        var target = DelayedTradeSchedule.SettleTarget(
                            DelayedTradeClock.Local(utcNow, DelayedTradeClock.Zone(market)),
                            options.Value
                        );
                        var targetSeenIntraday =
                            result.SessionDate < target
                            && await scope
                                .ServiceProvider.GetRequiredService<DelayedTradeFileCaptureRepository>()
                                .HasSessionCapture(market.Code, target, stoppingToken);
                        state.SettledThroughDate = DelayedTradeSchedule.SettledThrough(
                            state.SettledThroughDate,
                            result.SessionDate,
                            target,
                            targetSeenIntraday
                        );
                        settledDate = result.SessionDate;
                        break;
                    case DelayedTradeSettleOutcome.Refused:
                        error = $"settle refused: {result.Refusal}";
                        break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                logger.LogError(exception, "{Market} delayed-trades settle failed", market.Code);
                await errorReporter.Report(
                    ErrorSource.DelayedTradeScraper,
                    $"SettleSession({market.Code})",
                    exception
                );
            }
        }

        if (plan.Intraday)
        {
            state.LastIntradayPollUtc = utcNow;
            try
            {
                var result = await importer.PollIntraday(market, source, utcNow, stoppingToken);
                if (result.Outcome != DelayedTradeIntradayOutcome.NoSession)
                    capturedAt = utcNow;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                error ??= exception.Message;
                logger.LogError(exception, "{Market} delayed-trades poll failed", market.Code);
                await errorReporter.Report(
                    ErrorSource.DelayedTradeScraper,
                    $"PollIntraday({market.Code})",
                    exception
                );
            }
        }

        var registrations =
            scope.ServiceProvider.GetRequiredService<EquityMarketRegistrationRepository>();
        var row = await registrations.GetByCode(market.Code, stoppingToken);
        if (row == null)
            return;
        if (capturedAt != null)
            row.DelayedTradesCapturedAt = capturedAt;
        if (settledDate != null)
            row.DelayedTradesSessionDate = settledDate;
        row.DelayedTradesLastError = error is { Length: > 1000 } ? error[..1000] : error;
        row.UpdatedAt = DateTime.UtcNow;
        await registrations.SaveChanges();
    }

    private DelayedTradeMarketState State(string code)
    {
        if (!_states.TryGetValue(code, out var state))
        {
            state = new DelayedTradeMarketState();
            _states[code] = state;
        }
        return state;
    }
}
