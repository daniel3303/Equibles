using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Equibles.Worker;

public abstract class BaseScraperWorker : BackgroundService {
    protected readonly ILogger Logger;
    protected readonly IServiceScopeFactory ScopeFactory;
    protected readonly ErrorReporter ErrorReporter;

    protected abstract string WorkerName { get; }
    protected abstract TimeSpan SleepInterval { get; }
    protected abstract ErrorSource ErrorSource { get; }

    protected BaseScraperWorker(
        ILogger logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter
    ) {
        Logger = logger;
        ScopeFactory = scopeFactory;
        ErrorReporter = errorReporter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!ValidateConfiguration()) return;

        while (!stoppingToken.IsCancellationRequested) {
            Logger.LogInformation("{Worker} running at: {Time}", WorkerName, DateTimeOffset.Now);

            try {
                await DoWork(stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                Logger.LogInformation("{Worker} cancelled", WorkerName);
                return;
            } catch (Exception ex) {
                Logger.LogCritical(ex, "Critical error in {Worker}", WorkerName);
                await ErrorReporter.Report(ErrorSource, $"{WorkerName}.DoWork", ex.Message, ex.StackTrace);
            }

            Logger.LogInformation("{Worker} cycle complete. Sleeping for {Interval}",
                WorkerName, SleepInterval);
            await Task.Delay(SleepInterval, stoppingToken);
        }
    }

    protected virtual bool ValidateConfiguration() => true;

    protected abstract Task DoWork(CancellationToken stoppingToken);
}
