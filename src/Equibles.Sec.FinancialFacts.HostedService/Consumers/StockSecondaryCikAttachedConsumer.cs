using Equibles.Messaging.Attributes;
using Equibles.Messaging.Contracts.CommonStocks;
using Equibles.Sec.FinancialFacts.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.HostedService.Consumers;

/// <summary>
/// A newly attached CIK's facts are all OLDER than the stock's facts checkpoint
/// (<c>FinancialFactsSyncStatus.LastFiledDateSeen</c> tracks the newest filed date
/// seen, and a predecessor registrant stopped filing when the ticker moved), so
/// without a reset the import's nothing-new-since short-circuit would skip the
/// attached history forever. Deleting the status row makes the stock look never
/// synced: the next facts cycle re-imports the full multi-CIK history (upsert on
/// the natural key, so re-reading the primary's facts is idempotent). Deletion is
/// idempotent too — a duplicate event finds no row and does nothing.
/// </summary>
[Consumer]
public class StockSecondaryCikAttachedConsumer : IConsumer<StockSecondaryCikAttached>
{
    private readonly FinancialFactsSyncStatusRepository _syncStatusRepository;
    private readonly ILogger<StockSecondaryCikAttachedConsumer> _logger;

    public StockSecondaryCikAttachedConsumer(
        FinancialFactsSyncStatusRepository syncStatusRepository,
        ILogger<StockSecondaryCikAttachedConsumer> logger
    )
    {
        _syncStatusRepository = syncStatusRepository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<StockSecondaryCikAttached> context)
    {
        var cleared = await _syncStatusRepository
            .GetAll()
            .Where(s => s.CommonStockId == context.Message.CommonStockId)
            .ExecuteDeleteAsync(context.CancellationToken);

        _logger.LogInformation(
            "Secondary CIK {Cik} attached to {Ticker} — facts checkpoint {Outcome}",
            context.Message.Cik,
            context.Message.Ticker,
            cleared > 0 ? "reset for full re-import" : "was already absent"
        );
    }
}
