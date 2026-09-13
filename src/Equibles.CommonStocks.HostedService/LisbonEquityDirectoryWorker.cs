using Equibles.CommonStocks.HostedService.Services;
using Microsoft.Extensions.Configuration;

namespace Equibles.CommonStocks.HostedService;

public class LisbonEquityDirectoryWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<LisbonEquityDirectoryWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("EquityMarkets:LisbonEnabled"))
            return;
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromDays(1);
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var importer =
                    scope.ServiceProvider.GetRequiredService<EuronextEquityDirectoryImporter>();
                var result = await importer.ImportLisbon(stoppingToken);
                if (result.Failed > 0)
                    delay = TimeSpan.FromMinutes(15);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Lisbon equity directory refresh failed; retained identity is unchanged for unresolved source records"
                );
                delay = TimeSpan.FromMinutes(15);
            }
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
