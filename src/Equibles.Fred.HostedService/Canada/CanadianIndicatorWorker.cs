using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Fred.HostedService.Configuration;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Fred.HostedService.Canada;

// Reads the Bank of Canada's and Statistics Canada's curated series and release dates on the FRED lane's cadence.
// Neither publisher needs a key, so it runs whether or not a FRED key is configured.
public class CanadianIndicatorWorker : BaseScraperWorker
{
    protected override string WorkerName => "Canadian indicators";
    private readonly TimeSpan _sleepInterval;

    protected override TimeSpan SleepInterval => _sleepInterval;
    protected override ErrorSource ErrorSource => ErrorSource.FredScraper;
    protected override int ErrorReportThreshold => 3;

    public CanadianIndicatorWorker(
        ILogger<CanadianIndicatorWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<FredScraperOptions> options
    )
        : base(logger, scopeFactory, errorReporter)
    {
        _sleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
    }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await RunImport<CanadianIndicatorImportService>(stoppingToken);
        // After the series exist, so a first run can link them to their releases.
        await RunImport<CanadianReleaseCalendarImportService>(stoppingToken);
    }
}
