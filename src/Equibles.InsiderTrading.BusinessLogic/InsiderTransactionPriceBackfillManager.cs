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
/// Evaluates the not-yet-checked insider transactions — those whose
/// <see cref="InsiderTransaction.IsPriceValid"/> is still <c>null</c>. Each
/// row's reported price is cross-checked against the Yahoo unadjusted close
/// on the transaction date (most recent prior trading day for weekends /
/// holidays) via <see cref="InsiderTransactionPriceValidator"/>; implausible
/// rows are repaired (reported total ÷ shares) and the raw value preserved in
/// <see cref="InsiderTransaction.ReportedPricePerShare"/>.
///
/// Only null rows are touched, so a row is evaluated exactly once and re-runs
/// don't re-scan the whole table. Rows with no usable close stay null and are
/// retried on a later run. Triggered from the backoffice maintenance
/// dashboard; iterates in batches with a progress callback for the SSE bar.
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
        // Snapshot of the work-set size for the progress bar. The live parser
        // may insert more null (pending) rows while this runs; those land
        // behind the advancing cursor and are picked up on the next run, so
        // Processed can briefly nudge past Total — harmless and self-correcting.
        var result = new InsiderTransactionPriceBackfillResult
        {
            Total = await _transactionRepository.GetAll().CountAsync(t => t.IsPriceValid == null),
        };

        if (result.Total == 0)
            return result;

        _dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

        // Keyset (cursor) pagination on (TransactionDate, Id) over the
        // unevaluated rows. Ordering by date — not the random Guid Id — keeps
        // each batch inside a narrow date window, so FetchCloses pulls a small
        // price range per batch instead of every stock's full history (the
        // Id-ordered version scattered each batch across all dates, making
        // FetchCloses load decades of prices at a time). The
        // (IsPriceValid, TransactionDate) index backs both the filter and the
        // order. Rows left pending (still null) fall behind the advancing
        // cursor, so the run terminates; they're retried on the next run.
        var lastDate = DateOnly.MinValue;
        var lastId = Guid.Empty;
        while (true)
        {
            var batch = await _transactionRepository
                .GetAll()
                .Where(t => t.IsPriceValid == null)
                .Where(t =>
                    t.TransactionDate > lastDate || (t.TransactionDate == lastDate && t.Id > lastId)
                )
                .OrderBy(t => t.TransactionDate)
                .ThenBy(t => t.Id)
                .Take(BatchSize)
                .ToListAsync();

            if (batch.Count == 0)
                break;

            var closes = await FetchCloses(batch);

            foreach (var transaction in batch)
            {
                var key = (transaction.CommonStockId, transaction.TransactionDate);
                decimal? close = closes.TryGetValue(key, out var value) ? value : null;

                var evaluation = _validator.Evaluate(
                    transaction.ReportedPricePerShare,
                    transaction.Shares,
                    transaction.SecurityKind,
                    transaction.SecurityTitle,
                    close
                );

                transaction.PricePerShare = evaluation.EffectivePrice;
                transaction.IsPriceValid = evaluation.IsPriceValid;

                if (evaluation.IsPriceValid == null)
                    result.Pending++;
                else if (evaluation.WasRepaired)
                    result.Repaired++;
                else if (evaluation.IsPriceValid == true)
                    result.Valid++;
                else
                    result.Invalid++;
            }

            await _transactionRepository.SaveChanges();

            // Detach the saved batch so the change tracker doesn't grow to the
            // full unevaluated set — otherwise every SaveChanges re-scans an
            // ever-larger graph (quadratic) and memory balloons across the run.
            _dbContext.ChangeTracker.Clear();

            result.Processed += batch.Count;
            lastDate = batch[^1].TransactionDate;
            lastId = batch[^1].Id;

            _logger.LogInformation(
                "Insider price backfill: processed {Processed}/{Total}, repaired={Repaired}, invalid={Invalid}, pending={Pending}",
                result.Processed,
                result.Total,
                result.Repaired,
                result.Invalid,
                result.Pending
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
