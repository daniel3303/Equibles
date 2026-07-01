using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CorporateActions.BusinessLogic;

// Upserts captured cash-dividend events into CashDividend. Idempotent by
// (stock, ExDate): a re-run with the same events writes nothing. A changed
// amount for an existing ex-date is updated in place (Yahoo occasionally
// restates a dividend after declaration).
[Service]
public class CashDividendCaptureManager
{
    private readonly CashDividendRepository _dividendRepository;

    public CashDividendCaptureManager(CashDividendRepository dividendRepository)
    {
        _dividendRepository = dividendRepository;
    }

    public async Task<int> Capture(
        CommonStock stock,
        IReadOnlyCollection<CapturedDividend> dividends
    )
    {
        if (dividends == null || dividends.Count == 0)
            return 0;

        var existing = await _dividendRepository.GetByStock(stock.Id).ToListAsync();
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
                changes++;
            }
        }

        if (changes > 0)
            await _dividendRepository.SaveChanges();

        return changes;
    }
}
