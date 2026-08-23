using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService;

/// <summary>
/// Self-draining historical sweep for stored filing text. A pipeline-version bump refills the
/// work-set; once every refetchable EDGAR document is current, cycles become a cheap empty query.
/// </summary>
public class DocumentNormalizationBackfillWorker : BaseScraperWorker
{
    private readonly IConfiguration _configuration;
    private readonly DocumentNormalizationBackfillOptions _options;

    protected override string WorkerName => "Document normalization backfill";

    protected override TimeSpan SleepInterval => TimeSpan.FromMinutes(5);

    protected override TimeSpan ContinuationInterval =>
        TimeSpan.FromSeconds(_options.DrainIntervalSeconds);

    protected override ErrorSource ErrorSource => ErrorSource.DocumentScraper;

    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(10);

    public DocumentNormalizationBackfillWorker(
        ILogger<DocumentNormalizationBackfillWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<DocumentNormalizationBackfillOptions> options,
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
            "Document normalization backfill",
            treatWhitespaceAsAbsent: true
        );

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            Logger.LogDebug("Document normalization backfill disabled; skipping cycle.");
            return;
        }

        var batchSize = _options.BatchSize;
        await using var scope = ScopeFactory.CreateAsyncScope();
        var service =
            scope.ServiceProvider.GetRequiredService<DocumentNormalizationBackfillService>();
        var result = await service.Backfill(
            batchSize,
            _options.IncludeAllDocumentTypes,
            _options.PriorityAccessions,
            stoppingToken
        );

        if (batchSize > 0 && result.Processed >= batchSize && result.Failed < result.Processed)
        {
            RequestImmediateContinuation();
        }

        Logger.LogInformation(
            "Document normalization backfill cycle complete. Processed: {Processed}, Replaced: {Replaced}, Unchanged: {Unchanged}, Failed: {Failed}",
            result.Processed,
            result.Replaced,
            result.Unchanged,
            result.Failed
        );
    }
}
