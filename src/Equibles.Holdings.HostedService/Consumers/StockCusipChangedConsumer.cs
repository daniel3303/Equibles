using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Messaging.Attributes;
using Equibles.Messaging.Contracts.CommonStocks;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Consumers;

/// <summary>
/// When a CommonStock's CUSIP identity grows (FTD seeding a previously-null
/// CUSIP, a retired-CUSIP alias, or a listed-ticker CUSIP), the quarterly 13F
/// data sets that were already marked processed while the identity was
/// unresolvable hold no holdings for it. This consumer QUEUES a rescan by
/// adding the <see cref="ProcessedDataSet.RescanPendingFileName"/> sentinel;
/// <c>HoldingsScraperWorker</c> applies it at the start of its next cycle —
/// clearing the <see cref="ProcessedDataSet"/> ledger (keeping the backfill
/// guard) plus the open filing season's realtime <see cref="ProcessedFiling"/>
/// rows — and then re-imports everything.
///
/// The clear is deferred rather than applied here on purpose: an inline clear
/// restarts the scraper's multi-hour oldest-first walk from scratch, and the
/// FTD sweeps discover identities near-daily, so the walk was starved and the
/// newest quarters never healed (EquiblesCommercial#7163). Queuing lets the
/// in-flight walk complete; events arriving mid-walk coalesce into one pending
/// rescan that the next walk applies with every identity known by then.
/// Reprocessing is idempotent (upsert), so over-invalidation is safe.
/// </summary>
[Consumer]
public class StockCusipChangedConsumer : IConsumer<StockCusipChanged>
{
    private readonly ProcessedDataSetRepository _processedDataSetRepository;
    private readonly HoldingsRescanSignal _rescanSignal;
    private readonly ILogger<StockCusipChangedConsumer> _logger;

    public StockCusipChangedConsumer(
        ProcessedDataSetRepository processedDataSetRepository,
        HoldingsRescanSignal rescanSignal,
        ILogger<StockCusipChangedConsumer> logger
    )
    {
        _processedDataSetRepository = processedDataSetRepository;
        _rescanSignal = rescanSignal;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<StockCusipChanged> context)
    {
        var alreadyQueued = await _processedDataSetRepository
            .GetAll()
            .AnyAsync(
                r => r.FileName == ProcessedDataSet.RescanPendingFileName,
                context.CancellationToken
            );
        if (alreadyQueued)
        {
            // Coalesce: the pending rescan has not been applied yet, so it will
            // run with this event's identity too. No signal either — the event
            // that queued the sentinel already woke the worker.
            return;
        }

        _processedDataSetRepository.Add(
            new ProcessedDataSet { FileName = ProcessedDataSet.RescanPendingFileName }
        );
        try
        {
            await _processedDataSetRepository.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // A concurrent event queued the sentinel between the check and the
            // save (FileName is unique). The rescan is pending and that
            // consumer signalled the worker — nothing left to do.
            return;
        }

        // Wake the Holdings worker now (GH-852) instead of waiting up to its
        // 24h cycle; it applies the queued rescan at the start of that cycle.
        _rescanSignal.RequestRescan();

        _logger.LogInformation(
            "CUSIP change for {Ticker} ({Cusip}) queued a deferred 13F rescan; the holdings worker clears the ledgers and re-imports at its next cycle start",
            context.Message.Ticker,
            context.Message.Cusip
        );
    }
}
