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
// ExDate): same-day cash components are summed into that one row, a re-run with
// the same events writes nothing, and a changed total for an existing ex-date is
// updated in place (providers occasionally restate a dividend after declaration).
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

        var combinedDividends = CombineSameDateDividends(dividends);
        if (combinedDividends.Count == 0)
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

        foreach (var dividend in combinedDividends)
        {
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
            else if (CanSupersede(match.Source, dividend.Source))
            {
                var amountChanged = match.AmountPerShare != dividend.AmountPerShare;
                var sourceChanged = match.Source != dividend.Source;
                if (!amountChanged && !sourceChanged)
                    continue;

                if (amountChanged)
                {
                    match.AmountPerShare = dividend.AmountPerShare;
                    match.PriceAdjustmentAppliedAmountPerShare = null;
                    match.PriceAdjustmentAppliedTime = null;
                }

                match.Source = dividend.Source;
                changes++;
            }
        }

        if (changes > 0)
            await _dividendRepository.SaveChanges();

        await transaction.CommitAsync(cancellationToken);
        return changes;
    }

    // Manual values are operator-owned. Yahoo supplies the adjusted price history, so its
    // dividend amount must remain stable against a later generic external-reference refresh.
    private static bool CanSupersede(
        CashDividendSource currentSource,
        CashDividendSource incomingSource
    ) =>
        incomingSource == currentSource
        || SourcePriority(incomingSource) > SourcePriority(currentSource);

    private static int SourcePriority(CashDividendSource source) =>
        source switch
        {
            CashDividendSource.External => 0,
            CashDividendSource.Yahoo => 1,
            CashDividendSource.Manual => 2,
            _ => int.MinValue,
        };

    internal static List<CapturedDividend> CombineSameDateDividends(
        IReadOnlyCollection<CapturedDividend> dividends
    ) =>
        dividends
            .Where(dividend => dividend.AmountPerShare > 0)
            .GroupBy(dividend => dividend.ExDate)
            .Select(CombineSameDateDividend)
            .ToList();

    private static CapturedDividend CombineSameDateDividend(
        IGrouping<DateOnly, CapturedDividend> dividends
    )
    {
        var sources = dividends.Select(dividend => dividend.Source).Distinct().ToList();
        if (sources.Count != 1)
        {
            throw new InvalidOperationException(
                $"Cash dividends on {dividends.Key:yyyy-MM-dd} must come from one source per capture batch."
            );
        }

        return new CapturedDividend
        {
            ExDate = dividends.Key,
            AmountPerShare = dividends.Sum(dividend => dividend.AmountPerShare),
            Source = sources[0],
        };
    }
}
