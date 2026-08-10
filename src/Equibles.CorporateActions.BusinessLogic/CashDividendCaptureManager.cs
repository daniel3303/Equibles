using System.Data;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CorporateActions.BusinessLogic;

// Upserts captured cash-dividend events into CashDividend. The manager locks and
// revalidates the exact current primary listing before writing, so a company-sync
// reorder cannot attach one security's action to another. Idempotent by (stock,
// ExDate): a re-run with the same events writes nothing. A changed amount for an
// existing ex-date is updated in place (Yahoo occasionally restates a dividend
// after declaration).
[Service]
public class CashDividendCaptureManager
{
    private readonly CashDividendRepository _dividendRepository;
    private readonly CommonStockRepository _stockRepository;

    public CashDividendCaptureManager(
        CashDividendRepository dividendRepository,
        CommonStockRepository stockRepository
    )
    {
        _dividendRepository = dividendRepository;
        _stockRepository = stockRepository;
    }

    public async Task<int> Capture(
        Guid commonStockId,
        string listedTicker,
        IReadOnlyCollection<CapturedDividend> dividends,
        CancellationToken cancellationToken = default
    )
    {
        if (dividends == null || dividends.Count == 0)
            return 0;

        await using var transaction = await _dividendRepository.CreateTransaction(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        var stock = await _stockRepository.GetForUpdate(commonStockId, cancellationToken);
        var resolvedTicker = SecondaryTickerPolicy.ResolveListedTicker(stock, listedTicker);
        if (
            resolvedTicker == null
            || !string.Equals(resolvedTicker, stock.Ticker, StringComparison.OrdinalIgnoreCase)
        )
        {
            await transaction.RollbackAsync(cancellationToken);
            return 0;
        }

        var existing = await _dividendRepository
            .GetByStock(stock.Id)
            .ToListAsync(cancellationToken);
        var changes = 0;

        foreach (var dividend in dividends)
        {
            if (dividend.AmountPerShare <= 0)
                continue;

            var match = existing.FirstOrDefault(d => d.ExDate == dividend.ExDate);
            if (match == null)
            {
                _dividendRepository.Add(
                    new CashDividend
                    {
                        CommonStockId = stock.Id,
                        ExDate = dividend.ExDate,
                        AmountPerShare = dividend.AmountPerShare,
                        Source = dividend.Source,
                    }
                );
                changes++;
            }
            else if (match.AmountPerShare != dividend.AmountPerShare)
            {
                match.AmountPerShare = dividend.AmountPerShare;
                match.PriceAdjustmentAppliedAmountPerShare = null;
                match.PriceAdjustmentAppliedTime = null;
                changes++;
            }
        }

        if (changes > 0)
            await _dividendRepository.SaveChanges();

        await transaction.CommitAsync(cancellationToken);
        return changes;
    }
}
