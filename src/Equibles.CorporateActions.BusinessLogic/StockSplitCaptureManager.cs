using System.Data;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CorporateActions.BusinessLogic;

// Upserts captured split events into StockSplit. The manager locks and revalidates
// the exact current listing before writing, so a company-sync reorder cannot attach one
// security's action to another. Idempotent by (stock, listed ticker, EffectiveDate): a
// re-run with the same events writes nothing. A changed ratio for the same exact
// source series clears PriceAdjustmentAppliedTime for another reconciliation.
[Service]
public class StockSplitCaptureManager
{
    private readonly StockSplitRepository _splitRepository;
    private readonly CommonStockRepository _stockRepository;

    public StockSplitCaptureManager(
        StockSplitRepository splitRepository,
        CommonStockRepository stockRepository
    )
    {
        _splitRepository = splitRepository;
        _stockRepository = stockRepository;
    }

    public async Task<int> Capture(
        Guid commonStockId,
        string listedTicker,
        IReadOnlyCollection<CapturedSplit> splits,
        CancellationToken cancellationToken = default
    )
    {
        if (splits == null || splits.Count == 0)
            return 0;

        await using var transaction = await _splitRepository.CreateTransaction(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        var stock = await _stockRepository.GetForUpdate(commonStockId, cancellationToken);
        var resolvedTicker = SecondaryTickerPolicy.ResolveListedTicker(stock, listedTicker);
        if (resolvedTicker == null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return 0;
        }

        var existing = await _splitRepository.GetByStock(stock.Id).ToListAsync(cancellationToken);
        var changes = 0;

        foreach (var split in splits)
        {
            if (split.Numerator <= 0 || split.Denominator <= 0)
                continue;

            var isPrimary = string.Equals(
                resolvedTicker,
                stock.Ticker,
                StringComparison.OrdinalIgnoreCase
            );
            var match = existing.FirstOrDefault(s =>
                s.EffectiveDate == split.EffectiveDate
                && string.Equals(
                    s.PriceSeriesTicker,
                    resolvedTicker,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            match ??= isPrimary
                ? existing.FirstOrDefault(s =>
                    s.EffectiveDate == split.EffectiveDate && s.PriceSeriesTicker == null)
                : null;
            if (match == null)
            {
                match = new StockSplit
                {
                    CommonStockId = stock.Id,
                    PriceSeriesTicker = resolvedTicker,
                    EffectiveDate = split.EffectiveDate,
                    Numerator = split.Numerator,
                    Denominator = split.Denominator,
                    Source = split.Source,
                };
                _splitRepository.Add(match);
                existing.Add(match);
                changes++;
            }
            else if (
                match.PriceSeriesTicker == null
                || match.Numerator != split.Numerator
                || match.Denominator != split.Denominator
            )
            {
                match.PriceSeriesTicker = resolvedTicker;
                match.Numerator = split.Numerator;
                match.Denominator = split.Denominator;
                // Prices were adjusted for the old ratio — force a re-reconcile.
                match.PriceAdjustmentAppliedTime = null;
                changes++;
            }
        }

        if (changes > 0)
            await _splitRepository.SaveChanges();

        await transaction.CommitAsync(cancellationToken);
        return changes;
    }
}
