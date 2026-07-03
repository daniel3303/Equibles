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
/// Sweeps concept metadata (labels, documentation, debit/credit balance) from
/// each company's recent filings' MetaLinks artifact. A company is due when it
/// was never swept, when a newer filing arrived since its last sweep (new
/// filings introduce new extension concepts), or on the periodic refresh.
/// </summary>
public class ConceptMetadataWorker : BaseScraperWorker
{
    private readonly IConfiguration _configuration;
    private readonly ConceptMetadataOptions _options;

    protected override string WorkerName => "Concept metadata sweep";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.FinancialFactsScraper;

    // Light SEC walker (a few small JSON fetches per due company) — staggered
    // behind the heavy sweeps so it never competes for the EDGAR budget at boot.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(12);

    public ConceptMetadataWorker(
        ILogger<ConceptMetadataWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<ConceptMetadataOptions> options,
        IConfiguration configuration
    )
        : base(logger, scopeFactory, errorReporter)
    {
        SleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
        _options = options.Value;
        _configuration = configuration;
    }

    protected override bool ValidateConfiguration() =>
        ValidateSecContactEmail(
            _configuration,
            "Concept metadata sweep",
            treatWhitespaceAsAbsent: true
        );

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        List<Guid> dueStockIds;
        using (var scope = ScopeFactory.CreateScope())
        {
            var statusRepository =
                scope.ServiceProvider.GetRequiredService<FinancialFactsSyncStatusRepository>();
            var statuses = await statusRepository
                .GetAll()
                .Select(s => new
                {
                    s.CommonStockId,
                    s.LastFiledDateSeen,
                    s.ConceptMetadataCheckedAt,
                })
                .ToListAsync(stoppingToken);

            var refreshCutoff = DateTime.UtcNow.AddDays(-_options.RefreshDays);
            dueStockIds = statuses
                .Where(s =>
                    s.ConceptMetadataCheckedAt == null
                    || s.ConceptMetadataCheckedAt < refreshCutoff
                    || (
                        s.LastFiledDateSeen != null
                        && s.ConceptMetadataCheckedAt
                            < s.LastFiledDateSeen.Value.ToDateTime(
                                TimeOnly.MinValue,
                                DateTimeKind.Utc
                            )
                    )
                )
                // Never-swept companies first, then stalest sweeps.
                .OrderBy(s => s.ConceptMetadataCheckedAt ?? DateTime.MinValue)
                .Select(s => s.CommonStockId)
                .ToList();
        }

        if (dueStockIds.Count == 0)
            return;

        Logger.LogInformation("Concept metadata sweep: {Count} companies due", dueStockIds.Count);

        foreach (var stockId in dueStockIds)
        {
            stoppingToken.ThrowIfCancellationRequested();

            using var scope = ScopeFactory.CreateScope();
            var stockRepository = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
            var stock = await stockRepository.Get(stockId);
            if (stock == null)
                continue;

            var service = scope.ServiceProvider.GetRequiredService<ConceptMetadataService>();
            await service.ProcessStock(stock, stoppingToken);
        }
    }
}
