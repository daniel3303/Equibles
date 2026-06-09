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
/// Scrapes news and events from the RSS feeds of stocks whose IR site runs on
/// Nasdaq IR Insight, persisting them into IrNewsItem / IrEvent. Insertion is
/// idempotent by natural key, so re-running a cycle never duplicates rows.
/// </summary>
[Service]
public class NasdaqIrInsightScraperService : IImporter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NasdaqIrInsightFeedClient _feedClient;
    private readonly ErrorReporter _errorReporter;
    private readonly ILogger<NasdaqIrInsightScraperService> _logger;
    private readonly NasdaqIrInsightScraperOptions _options;

    public NasdaqIrInsightScraperService(
        IServiceScopeFactory scopeFactory,
        NasdaqIrInsightFeedClient feedClient,
        ErrorReporter errorReporter,
        ILogger<NasdaqIrInsightScraperService> logger,
        IOptions<NasdaqIrInsightScraperOptions> options
    )
    {
        _scopeFactory = scopeFactory;
        _feedClient = feedClient;
        _errorReporter = errorReporter;
        _logger = logger;
        _options = options.Value;
    }

    public async Task Import(CancellationToken cancellationToken)
    {
        var batch = await LoadCandidates(cancellationToken);
        if (batch.Count == 0)
        {
            _logger.LogInformation("Nasdaq IR Insight scrape: no classified stocks to scrape");
            return;
        }

        _logger.LogInformation("Nasdaq IR Insight scrape: scraping {Count} stocks", batch.Count);

        var inserted = 0;
        foreach (var candidate in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                inserted += await ScrapeStock(candidate, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping IR feeds for {Ticker}", candidate.Ticker);
                await _errorReporter.Report(
                    ErrorSource.InvestorRelationsScraper,
                    $"Scrape({candidate.Ticker})",
                    ex.Message,
                    ex.StackTrace
                );
            }
        }

        _logger.LogInformation(
            "Nasdaq IR Insight scrape complete. Inserted {Count} new IR rows",
            inserted
        );
    }

    private async Task<int> ScrapeStock(
        CandidateStock candidate,
        CancellationToken cancellationToken
    )
    {
        var stock = new CommonStock { Id = candidate.Id };

        var newsXml = await _feedClient.Fetch(
            candidate.IrUrl,
            NasdaqIrInsightFeedClient.NewsFeedPath,
            cancellationToken
        );
        var eventsXml = await _feedClient.Fetch(
            candidate.IrUrl,
            NasdaqIrInsightFeedClient.EventsFeedPath,
            cancellationToken
        );

        var news = newsXml == null ? [] : NasdaqIrInsightFeedParser.ParseNews(newsXml);
        var events = eventsXml == null ? [] : NasdaqIrInsightFeedParser.ParseEvents(eventsXml);
        if (news.Count == 0 && events.Count == 0)
            return 0;

        using var scope = _scopeFactory.CreateScope();
        var newsRepo = scope.ServiceProvider.GetRequiredService<IrNewsItemRepository>();
        var eventRepo = scope.ServiceProvider.GetRequiredService<IrEventRepository>();

        var existingUrls = (
            await newsRepo.GetByStock(stock).Select(n => n.Url).ToListAsync(cancellationToken)
        ).ToHashSet();
        var existingEvents = (
            await eventRepo
                .GetByStock(stock)
                .Select(e => new { e.StartDateTime, e.Title })
                .ToListAsync(cancellationToken)
        )
            .Select(e => (e.StartDateTime, e.Title))
            .ToHashSet();

        var added = 0;
        foreach (var item in news)
        {
            if (!existingUrls.Add(item.Url))
                continue;
            newsRepo.Add(
                new IrNewsItem
                {
                    CommonStockId = candidate.Id,
                    Title = item.Title,
                    Url = item.Url,
                    Summary = item.Summary,
                    PublishedAt = item.PublishedAtUtc,
                    Source = IrPlatformType.NasdaqIrInsight,
                }
            );
            added++;
        }

        foreach (var ev in events)
        {
            if (!existingEvents.Add((ev.StartDateTimeUtc, ev.Title)))
                continue;
            eventRepo.Add(
                new IrEvent
                {
                    CommonStockId = candidate.Id,
                    Title = ev.Title,
                    StartDateTime = ev.StartDateTimeUtc,
                    Type = ev.Type,
                    Url = ev.Url,
                    Source = IrPlatformType.NasdaqIrInsight,
                }
            );
            added++;
        }

        if (added > 0)
            await newsRepo.SaveChanges();

        return added;
    }

    private async Task<List<CandidateStock>> LoadCandidates(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        var rows = await repo.GetAll()
            .Where(s =>
                s.IrPlatformType == IrPlatformType.NasdaqIrInsight && s.InvestorRelationsUrl != null
            )
            .OrderBy(s => s.Ticker)
            .Take(_options.BatchSize)
            .Select(s => new
            {
                s.Id,
                s.Ticker,
                s.InvestorRelationsUrl,
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new CandidateStock(r.Id, r.Ticker, r.InvestorRelationsUrl))
            .ToList();
    }

    private sealed record CandidateStock(Guid Id, string Ticker, string IrUrl);
}
