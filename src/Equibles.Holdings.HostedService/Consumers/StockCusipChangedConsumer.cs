using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Messaging.Attributes;
using Equibles.Messaging.Contracts.CommonStocks;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Consumers;

/// <summary>
/// When a CommonStock's CUSIP is set/changed (typically FTD seeding a
/// previously-null CUSIP), the quarterly 13F data sets that were already
/// marked processed while the stock was unresolvable hold no holdings for it.
/// This consumer clears the <see cref="ProcessedDataSet"/> ledger (keeping a
/// guard sentinel so the worker's first-boot backfill seeding doesn't re-skip
/// history) so <c>HoldingsScraperWorker</c> re-imports every data set on its
/// next cycle and backfills the now-resolvable stock. Reprocessing is
/// idempotent (upsert), so over-invalidation is safe; the FTD cold-start
/// burst of events collapses to a no-op once already cleared.
/// </summary>
[Consumer]
public class StockCusipChangedConsumer : IConsumer<StockCusipChanged>
{
    private readonly ProcessedDataSetRepository _processedDataSetRepository;
    private readonly ILogger<StockCusipChangedConsumer> _logger;

    public StockCusipChangedConsumer(
        ProcessedDataSetRepository processedDataSetRepository,
        ILogger<StockCusipChangedConsumer> logger
    )
    {
        _processedDataSetRepository = processedDataSetRepository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<StockCusipChanged> context)
    {
        var rows = await _processedDataSetRepository
            .GetAll()
            .ToListAsync(context.CancellationToken);

        var realRows = rows.Where(r => r.FileName != ProcessedDataSet.BackfillGuardFileName)
            .ToList();
        var hasGuard = rows.Any(r => r.FileName == ProcessedDataSet.BackfillGuardFileName);

        if (realRows.Count == 0 && hasGuard)
        {
            // Already invalidated (e.g. a prior event in the FTD seeding burst).
            return;
        }

        if (realRows.Count > 0)
        {
            _processedDataSetRepository.Delete(realRows);
        }

        if (!hasGuard)
        {
            _processedDataSetRepository.Add(
                new ProcessedDataSet { FileName = ProcessedDataSet.BackfillGuardFileName }
            );
        }

        await _processedDataSetRepository.SaveChanges();

        _logger.LogInformation(
            "CUSIP change for {Ticker} ({Cusip}) invalidated {Count} processed 13F data set(s); the quarterly holdings worker will re-import and backfill on its next cycle",
            context.Message.Ticker,
            context.Message.Cusip,
            realRows.Count
        );
    }
}
