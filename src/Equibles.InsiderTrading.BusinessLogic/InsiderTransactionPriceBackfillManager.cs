using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.InsiderTrading.BusinessLogic.Models;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Equibles.InsiderTrading.BusinessLogic;

/// <summary>
/// One-off recompute of <see cref="InsiderTransaction.IsPriceValid"/> across
/// every row. Cross-checks the filer-reported <c>PricePerShare</c> against
/// the Yahoo unadjusted close on the transaction date (most recent prior
/// trading day if the transaction date fell on a weekend / holiday) and
/// flips the flag according to <see cref="InsiderTransactionPriceValidator"/>.
///
/// Triggered from the backoffice maintenance dashboard after a parser fix
/// lands. Idempotent and re-entrant — safe to click again. Iterates in
/// batches with a progress callback so the UI can show an SSE-driven bar.
/// </summary>
[Service]
public class InsiderTransactionPriceBackfillManager
{
    private const int BatchSize = 1000;

    /// <summary>
    /// How far back to look from the earliest TransactionDate in a batch
    /// when fetching candidate closes. Long enough to skip the longest
    /// real holiday run (Thanksgiving week + adjacent weekends ≈ 5
    /// trading-day gap; 10 calendar days is comfortably above that).
    /// </summary>
    private const int CloseLookbackDays = 10;

    private readonly InsiderTransactionRepository _transactionRepository;
    private readonly DailyStockPriceRepository _dailyStockPriceRepository;
    private readonly InsiderTransactionPriceValidator _validator;
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly ILogger<InsiderTransactionPriceBackfillManager> _logger;

    public InsiderTransactionPriceBackfillManager(
        InsiderTransactionRepository transactionRepository,
        DailyStockPriceRepository dailyStockPriceRepository,
        InsiderTransactionPriceValidator validator,
        EquiblesFinancialDbContext dbContext,
        ILogger<InsiderTransactionPriceBackfillManager> logger
    )
    {
        _transactionRepository = transactionRepository;
        _dailyStockPriceRepository = dailyStockPriceRepository;
        _validator = validator;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<InsiderTransactionPriceBackfillResult> Run(
        Func<InsiderTransactionPriceBackfillResult, Task> onProgress = null
    )
    {
        var result = new InsiderTransactionPriceBackfillResult
        {
            Total = await _transactionRepository.GetAll().CountAsync(),
        };

        if (result.Total == 0)
            return result;

        _dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

        // Keyset (cursor) pagination on Id. Postgres OFFSET scans and
        // discards N rows before returning the M for that batch, so a
        // Skip(N).Take(M) loop is O(N²) — the last batch in a 3M-row table
        // is thousands of times slower than the first. Filtering by
        // `Id > lastSeenId` instead lets the index seek straight to the
        // next page, keeping every batch roughly the same speed and the
        // whole pass O(N). Npgsql translates `Guid > Guid` to PostgreSQL's
        // native `uuid > uuid` comparison.
        var lastSeenId = Guid.Empty;
        while (true)
        {
            var batch = await _transactionRepository
                .GetAll()
                .Where(t => t.Id > lastSeenId)
                .OrderBy(t => t.Id)
                .Take(BatchSize)
                .ToListAsync();

            if (batch.Count == 0)
                break;

            var closes = await FetchCloses(batch);

            foreach (var transaction in batch)
            {
                var key = (transaction.CommonStockId, transaction.TransactionDate);
                decimal? close = closes.TryGetValue(key, out var value) ? value : null;

                var wasValid = transaction.IsPriceValid;
                var nowValid = _validator.IsPlausible(
                    transaction.PricePerShare,
                    transaction.SecurityTitle,
                    close
                );

                if (wasValid != nowValid)
                {
                    transaction.IsPriceValid = nowValid;
                }
                if (!nowValid)
                    result.MarkedInvalid++;
                else
                    result.MarkedValid++;
            }

            await _transactionRepository.SaveChanges();
            result.Processed += batch.Count;
            lastSeenId = batch[^1].Id;

            _logger.LogInformation(
                "Insider price backfill: processed {Processed}/{Total}, invalid={Invalid}",
                result.Processed,
                result.Total,
                result.MarkedInvalid
            );

            if (onProgress != null)
                await onProgress(result);
        }

        return result;
    }

    /// <summary>
    /// Fetch one close per distinct (CommonStockId, TransactionDate) — the
    /// most recent <see cref="DailyStockPrice.Close"/> on or before that
    /// date. Uses the unadjusted Close, never AdjustedClose: a reverse
    /// split between the transaction date and today would otherwise shift
    /// the historical adjusted close away from the raw price the filer
    /// reported and produce false rejections.
    /// </summary>
    private async Task<Dictionary<(Guid, DateOnly), decimal>> FetchCloses(
        List<InsiderTransaction> batch
    )
    {
        var stockIds = batch.Select(t => t.CommonStockId).Distinct().ToList();
        var maxDate = batch.Max(t => t.TransactionDate);
        var minDate = batch.Min(t => t.TransactionDate).AddDays(-CloseLookbackDays);

        var rawPrices = await _dailyStockPriceRepository
            .GetAll()
            .Where(p =>
                stockIds.Contains(p.CommonStockId) && p.Date >= minDate && p.Date <= maxDate
            )
            .Select(p => new
            {
                p.CommonStockId,
                p.Date,
                p.Close,
            })
            .ToListAsync();

        var byStock = rawPrices
            .GroupBy(p => p.CommonStockId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Date).ToList());

        var result = new Dictionary<(Guid, DateOnly), decimal>();
        foreach (var transaction in batch)
        {
            var key = (transaction.CommonStockId, transaction.TransactionDate);
            if (result.ContainsKey(key))
                continue;
            if (!byStock.TryGetValue(transaction.CommonStockId, out var stockPrices))
                continue;
            var match = stockPrices.FirstOrDefault(p => p.Date <= transaction.TransactionDate);
            if (match != null)
                result[key] = match.Close;
        }
        return result;
    }
}
