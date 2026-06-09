using System.Linq.Expressions;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.HostedService.Configuration;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Fills in <c>CommonStock.InvestorRelationsUrl</c> for stocks that have a known
/// website but no discovered IR page yet, one bounded batch per cycle.
/// </summary>
[Service]
public class InvestorRelationsDiscoveryService : IImporter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly InvestorRelationsProbeClient _probeClient;
    private readonly ErrorReporter _errorReporter;
    private readonly ILogger<InvestorRelationsDiscoveryService> _logger;
    private readonly InvestorRelationsDiscoveryOptions _options;

    public InvestorRelationsDiscoveryService(
        IServiceScopeFactory scopeFactory,
        InvestorRelationsProbeClient probeClient,
        ErrorReporter errorReporter,
        ILogger<InvestorRelationsDiscoveryService> logger,
        IOptions<InvestorRelationsDiscoveryOptions> options
    )
    {
        _scopeFactory = scopeFactory;
        _probeClient = probeClient;
        _errorReporter = errorReporter;
        _logger = logger;
        _options = options.Value;
    }

    public async Task Import(CancellationToken cancellationToken)
    {
        var batch = await LoadCandidates(cancellationToken);
        if (batch.Count == 0)
        {
            _logger.LogInformation(
                "Investor relations discovery: no stocks with a website pending discovery"
            );
            return;
        }

        _logger.LogInformation("Investor relations discovery: probing {Count} stocks", batch.Count);

        var discovered = 0;
        foreach (var candidate in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await _probeClient.Discover(
                    candidate.Website,
                    _options.CandidatePaths,
                    _options.CandidateSubdomains,
                    cancellationToken
                );
                // Definitive miss — every candidate was probed and none validated. Stamp
                // the attempt so the stock backs off for the cooldown window instead of
                // re-occupying a batch slot (and starving the rest of the universe) every
                // cycle. Transient errors below deliberately skip the stamp.
                if (result == null)
                {
                    await MarkChecked(candidate.Id);
                    continue;
                }

                if (await Persist(candidate.Id, result))
                {
                    discovered++;
                    _logger.LogDebug(
                        "Discovered investor relations page for {Ticker}: {Url} ({Platform})",
                        candidate.Ticker,
                        result.Url,
                        result.Platform
                    );
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error discovering investor relations page for {Ticker}",
                    candidate.Ticker
                );
                await _errorReporter.Report(
                    ErrorSource.InvestorRelationsDiscovery,
                    $"Discover({candidate.Ticker})",
                    ex.Message,
                    ex.StackTrace
                );
            }
        }

        _logger.LogInformation(
            "Investor relations discovery complete. Discovered {Count} new IR pages",
            discovered
        );
    }

    private async Task<List<CandidateStock>> LoadCandidates(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        var cutoff = DateTime.UtcNow.AddDays(-_options.ProbeCooldownDays);
        var rows = await repo.GetAll()
            .Where(PendingDiscovery(cutoff))
            .OrderBy(s => s.Ticker)
            .Take(_options.BatchSize)
            .Select(s => new
            {
                s.Id,
                s.Ticker,
                s.Website,
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new CandidateStock(r.Id, r.Ticker, r.Website)).ToList();
    }

    private async Task<bool> Persist(Guid commonStockId, IrDiscoveryResult result)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        var stock = await repo.Get(commonStockId);
        // The stock may have been deleted, or filled in by an overlapping run,
        // between loading the batch and persisting — leave an existing value alone.
        if (stock == null || stock.InvestorRelationsUrl != null)
            return false;

        stock.InvestorRelationsUrl = result.Url;
        stock.IrPlatformType = result.Platform;
        stock.InvestorRelationsCheckedAt = DateTime.UtcNow;
        await repo.SaveChanges();
        return true;
    }

    private async Task MarkChecked(Guid commonStockId)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        var stock = await repo.Get(commonStockId);
        if (stock == null)
            return;

        stock.InvestorRelationsCheckedAt = DateTime.UtcNow;
        await repo.SaveChanges();
    }

    /// <summary>
    /// Stocks eligible for an IR discovery probe: a known website, no discovered IR
    /// page yet, and no probe since <paramref name="cutoff"/> (never-probed stocks are
    /// always eligible).
    /// </summary>
    public static Expression<Func<CommonStock, bool>> PendingDiscovery(DateTime cutoff)
    {
        return s =>
            s.Website != null
            && s.Website != ""
            && s.InvestorRelationsUrl == null
            && (s.InvestorRelationsCheckedAt == null || s.InvestorRelationsCheckedAt < cutoff);
    }

    private sealed record CandidateStock(Guid Id, string Ticker, string Website);
}
