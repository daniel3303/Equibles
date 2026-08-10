using Equibles.CommonStocks.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.FinancialFacts.HostedService;

/// <summary>
/// Walks every tracked company with a CIK and ingests its SEC Company Facts.
/// </summary>
public class FinancialFactsScraperWorker : BaseScraperWorker
{
    private readonly IConfiguration _configuration;
    private readonly TimeSpan _recheckInterval;

    protected override string WorkerName => "Financial facts scraper";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.FinancialFactsScraper;

    // Heaviest SEC walker (one request per tracked company) — staggered last so it
    // doesn't drain the shared EDGAR budget before the 13F real-time sweep runs.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(8);

    public FinancialFactsScraperWorker(
        ILogger<FinancialFactsScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<FinancialFactsScraperOptions> options,
        IConfiguration configuration
    )
        : base(logger, scopeFactory, errorReporter)
    {
        SleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
        _recheckInterval = TimeSpan.FromHours(options.Value.RecheckIntervalHours);
        _configuration = configuration;
    }

    protected override bool ValidateConfiguration() =>
        ValidateSecContactEmail(
            _configuration,
            "Financial facts scraper",
            treatWhitespaceAsAbsent: true
        );

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        List<Guid> allStockIds;
        Dictionary<Guid, DateTime> lastCheckedByStock;
        Dictionary<Guid, int> importerVersionByStock;
        using (var scope = ScopeFactory.CreateScope())
        {
            var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
            allStockIds = await stockRepo
                .GetAll()
                .Where(s => s.Cik != null && s.Cik != "")
                .Select(s => s.Id)
                .ToListAsync(stoppingToken);

            var syncStatusRepo =
                scope.ServiceProvider.GetRequiredService<FinancialFactsSyncStatusRepository>();
            var syncStatuses = await syncStatusRepo
                .GetAll()
                .AsNoTracking()
                .Select(s => new
                {
                    s.CommonStockId,
                    s.LastCheckedAt,
                    s.ImporterVersion,
                })
                .ToListAsync(stoppingToken);
            lastCheckedByStock = syncStatuses.ToDictionary(
                s => s.CommonStockId,
                s => s.LastCheckedAt
            );
            importerVersionByStock = syncStatuses.ToDictionary(
                s => s.CommonStockId,
                s => s.ImporterVersion
            );
        }

        if (allStockIds.Count == 0)
        {
            Logger.LogInformation(
                "Financial facts scraper: no companies with a CIK yet (company sync pending) — will retry soon"
            );
            RequestRetrySoon();
            return;
        }

        // Every visit downloads the company's full Company Facts JSON (the
        // LastFiledDateSeen checkpoint can only say "nothing new" after the
        // download), and the sweep restarts from scratch on every host restart.
        // Skipping companies checked within the recheck window makes a restart
        // resume where the aborted sweep left off — never-checked first, then
        // stalest first — instead of re-downloading the whole universe.
        var stockIds = SelectDueStocks(
            allStockIds,
            lastCheckedByStock,
            importerVersionByStock,
            DateTime.UtcNow - _recheckInterval
        );

        Logger.LogInformation(
            "Financial facts scraper: {Due} of {Total} companies due (rest checked within {Window})",
            stockIds.Count,
            allStockIds.Count,
            _recheckInterval
        );

        foreach (var stockId in stockIds)
        {
            stoppingToken.ThrowIfCancellationRequested();

            using var scope = ScopeFactory.CreateScope();
            var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
            var stock = await stockRepo.Get(stockId);
            if (stock == null)
                continue;

            var importService =
                scope.ServiceProvider.GetRequiredService<FinancialFactsImportService>();
            await importService.Import(stock, stoppingToken);
        }
    }

    /// <summary>
    /// Companies due for a facts check: never checked (no sync-status row),
    /// imported under an older contract version, or checked before the cutoff.
    /// Never-checked first, then outdated imports, then ordinary stale rows so
    /// a release repairs the corpus promptly without losing rolling-walk order.
    /// </summary>
    internal static List<Guid> SelectDueStocks(
        List<Guid> stockIds,
        Dictionary<Guid, DateTime> lastCheckedByStock,
        Dictionary<Guid, int> importerVersionByStock,
        DateTime cutoff
    ) =>
        stockIds
            .Where(id =>
                !lastCheckedByStock.TryGetValue(id, out var lastChecked)
                || !importerVersionByStock.TryGetValue(id, out var importerVersion)
                || importerVersion < FinancialFactsImportService.CurrentImporterVersion
                || lastChecked < cutoff
            )
            .OrderBy(id => lastCheckedByStock.ContainsKey(id) ? 1 : 0)
            .ThenBy(id =>
                importerVersionByStock.TryGetValue(id, out var importerVersion)
                && importerVersion >= FinancialFactsImportService.CurrentImporterVersion
                    ? 1
                    : 0
            )
            .ThenBy(id => lastCheckedByStock.GetValueOrDefault(id, DateTime.MinValue))
            .ToList();
}
