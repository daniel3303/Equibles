using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService;

/// <summary>
/// Drains <see cref="XbrlDocumentSetRecaptureService"/>: re-reads captured filings that kept only
/// the cover of an inline document set. Self-retiring: once every candidate is rewritten after the
/// cutoff the selecting query is empty and the cycle idles. Shares the EDGAR budget with the live
/// scrapers.
/// </summary>
public class XbrlDocumentSetRecaptureWorker : BaseScraperWorker
{
    private readonly IConfiguration _configuration;
    private readonly XbrlCaptureOptions _options;

    protected override string WorkerName => "XBRL document-set recapture";

    protected override TimeSpan SleepInterval => TimeSpan.FromHours(1);

    protected override TimeSpan ContinuationInterval =>
        TimeSpan.FromSeconds(_options.RecaptureDrainIntervalSeconds);

    protected override ErrorSource ErrorSource => ErrorSource.DocumentScraper;

    // Yield to the live SEC scrapers at deploy time before spending the shared EDGAR budget.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(10);

    public XbrlDocumentSetRecaptureWorker(
        ILogger<XbrlDocumentSetRecaptureWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<XbrlCaptureOptions> options,
        IConfiguration configuration
    )
        : base(logger, scopeFactory, errorReporter)
    {
        _options = options.Value;
        _configuration = configuration;
    }

    protected override bool ValidateConfiguration() =>
        ValidateSecContactEmail(
            _configuration,
            "XBRL document-set recapture",
            treatWhitespaceAsAbsent: true
        );

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
            return;

        var batchSize = _options.RecaptureBatchSize;
        await using var scope = ScopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<XbrlDocumentSetRecaptureService>();
        var result = await service.Recapture(batchSize, stoppingToken);

        if (batchSize > 0 && result.Processed >= batchSize && result.Failed < result.Processed)
            RequestImmediateContinuation();

        Logger.LogInformation(
            "XBRL document-set recapture cycle complete. Processed: {Processed}, Grown: {Grown}, "
                + "Unchanged: {Unchanged}, Failed: {Failed}",
            result.Processed,
            result.Grown,
            result.Unchanged,
            result.Failed
        );
    }
}
