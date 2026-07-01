using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CorporateActions.BusinessLogic;

// Upserts captured split events into StockSplit. Idempotent by (stock,
// EffectiveDate): a re-run with the same events writes nothing. A changed ratio
// for an existing date is updated and its PriceAdjustmentAppliedTime cleared, so
// the back-adjustment pass re-reconciles historical prices for the new ratio.
[Service]
public class StockSplitCaptureManager
{
    private readonly StockSplitRepository _splitRepository;

    public StockSplitCaptureManager(StockSplitRepository splitRepository)
    {
        _splitRepository = splitRepository;
    }

    public async Task<int> Capture(CommonStock stock, IReadOnlyCollection<CapturedSplit> splits)
    {
        if (splits == null || splits.Count == 0)
            return 0;

        var existing = await _splitRepository.GetByStock(stock.Id).ToListAsync();
        var changes = 0;

        foreach (var split in splits)
        {
            if (split.Denominator <= 0)
                continue;

            var match = existing.FirstOrDefault(s => s.EffectiveDate == split.EffectiveDate);
            if (match == null)
            {
                _splitRepository.Add(
                    new StockSplit
                    {
                        CommonStockId = stock.Id,
                        EffectiveDate = split.EffectiveDate,
                        Numerator = split.Numerator,
                        Denominator = split.Denominator,
                        Source = split.Source,
                    }
                );
                changes++;
            }
            else if (match.Numerator != split.Numerator || match.Denominator != split.Denominator)
            {
                match.Numerator = split.Numerator;
                match.Denominator = split.Denominator;
                // Prices were adjusted for the old ratio — force a re-reconcile.
                match.PriceAdjustmentAppliedTime = null;
                changes++;
            }
        }

        if (changes > 0)
            await _splitRepository.SaveChanges();

        return changes;
    }
}
