using Equibles.CommonStocks.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Equibles.Sec.FinancialFacts.HostedService.Services;
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

    protected override string WorkerName => "Financial facts scraper";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.FinancialFactsScraper;

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
        _configuration = configuration;
    }

    protected override bool ValidateConfiguration()
    {
        if (string.IsNullOrEmpty(_configuration["Sec:ContactEmail"]))
        {
            Logger.LogWarning(
                "Financial facts scraper stopped: SEC_CONTACT_EMAIL not configured. Set it in your .env file."
            );
            return false;
        }
        return true;
    }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        List<Guid> stockIds;
        using (var scope = ScopeFactory.CreateScope())
        {
            var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
            stockIds = await stockRepo
                .GetAll()
                .Where(s => s.Cik != null && s.Cik != "")
                .OrderBy(s => s.Id)
                .Select(s => s.Id)
                .ToListAsync(stoppingToken);
        }

        if (stockIds.Count == 0)
        {
            Logger.LogInformation(
                "Financial facts scraper: no companies with a CIK yet (company sync pending) — will retry soon"
            );
            RequestRetrySoon();
            return;
        }

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
}
