using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
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
/// row's reported price is cross-checked against the stored close on the
/// transaction date (most recent prior trading day for weekends / holidays)
/// via <see cref="InsiderTransactionPriceValidator"/>. The stored close is on
/// TODAY'S split-adjusted basis while the filed price is on the transaction
/// date's basis, so each check carries the split factor and runs on both
/// bases; implausible rows are repaired (reported total ÷ shares) only inside
/// the session's price band, and the raw value is preserved in
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
    private readonly StockSplitRepository _stockSplitRepository;
    private readonly InsiderTransactionPriceValidator _validator;
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly ILogger<InsiderTransactionPriceBackfillManager> _logger;

    public InsiderTransactionPriceBackfillManager(
        InsiderTransactionRepository transactionRepository,
        DailyStockPriceRepository dailyStockPriceRepository,
        StockSplitRepository stockSplitRepository,
        InsiderTransactionPriceValidator validator,
        EquiblesFinancialDbContext dbContext,
        ILogger<InsiderTransactionPriceBackfillManager> logger
    )
    {
        _transactionRepository = transactionRepository;
        _dailyStockPriceRepository = dailyStockPriceRepository;
        _stockSplitRepository = stockSplitRepository;
        _validator = validator;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<InsiderTransactionPriceBackfillResult> Run(
        Func<InsiderTransactionPriceBackfillResult, Task> onProgress = null,
        CancellationToken cancellationToken = default
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
            // Between batches only: a granted stop lands after the current batch's
            // SaveChanges, so no evaluation is half-persisted and the next run resumes
            // from the surviving null rows.
            cancellationToken.ThrowIfCancellationRequested();
            var batch = await _transactionRepository
                .GetAll()
                .Where(t => t.IsPriceValid == null)
                .Where(t =>
                    t.TransactionDate > lastDate || (t.TransactionDate == lastDate && t.Id > lastId)
                )
                .OrderBy(t => t.TransactionDate)
                .ThenBy(t => t.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
                break;

            var bars = await FetchBars(batch);
            var (splitsByStock, identityByStock) = await FetchSplitContext(batch);

            foreach (var transaction in batch)
            {
                var key = (transaction.CommonStockId, transaction.TransactionDate);
                bars.TryGetValue(key, out var barRow);
                splitsByStock.TryGetValue(transaction.CommonStockId, out var splits);
                identityByStock.TryGetValue(transaction.CommonStockId, out var identity);

                var bar = InsiderDailyBars.Build(
                    barRow?.Close,
                    barRow?.Low,
                    barRow?.High,
                    transaction.TransactionDate,
                    splits ?? [],
                    identity?.Ticker,
                    identity?.SecondaryTickers
                );

                var evaluation = _validator.Evaluate(
                    transaction.ReportedPricePerShare,
                    transaction.Shares,
                    transaction.SecurityKind,
                    transaction.SecurityTitle,
                    bar,
                    transaction.Notes
                );

                transaction.PricePerShare = evaluation.EffectivePrice;
                transaction.IsPriceValid = evaluation.IsPriceValid;
                transaction.PriceWasRepaired = evaluation.WasRepaired;

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

    private sealed record BarRow(decimal Close, decimal Low, decimal High);

    private sealed record StockIdentity(
        string Ticker,
        IReadOnlyCollection<string> SecondaryTickers
    );

    /// <summary>
    /// Fetch one bar per distinct (CommonStockId, TransactionDate) — the most
    /// recent <see cref="DailyStockPrice"/> on or before that date. The stored
    /// Close/Low/High are on TODAY'S split-adjusted basis (the split
    /// reconciliation rewrites the whole listed series), which is exactly why
    /// the evaluation carries the split factor rather than treating the close
    /// as the raw price the filer saw.
    /// </summary>
    private async Task<Dictionary<(Guid, DateOnly), BarRow>> FetchBars(
        List<InsiderTransaction> batch
    )
    {
        var stockIds = batch.Select(t => t.CommonStockId).Distinct().ToList();
        var maxDate = batch.Max(t => t.TransactionDate);
        var minDate = batch.Min(t => t.TransactionDate).AddDays(-CloseLookbackDays);

        var rawPrices = await _dailyStockPriceRepository
            .GetAll()
            .Where(p =>
                stockIds.Contains(p.CommonStockId)
                && p.Date >= minDate
                && p.Date <= maxDate
                && p.Volume > 0
            )
            .Select(p => new
            {
                p.CommonStockId,
                p.Date,
                p.Close,
                p.Low,
                p.High,
            })
            .ToListAsync();

        var byStock = rawPrices
            .GroupBy(p => p.CommonStockId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Date).ToList());

        var result = new Dictionary<(Guid, DateOnly), BarRow>();
        foreach (var transaction in batch)
        {
            var key = (transaction.CommonStockId, transaction.TransactionDate);
            if (result.ContainsKey(key))
                continue;
            if (!byStock.TryGetValue(transaction.CommonStockId, out var stockPrices))
                continue;
            var match = stockPrices.FirstOrDefault(p => p.Date <= transaction.TransactionDate);
            if (match != null)
                result[key] = new BarRow(match.Close, match.Low, match.High);
        }
        return result;
    }

    /// <summary>
    /// Splits and ticker identity per stock in the batch — the inputs the
    /// split-basis resolver needs to restate the stored close onto each
    /// transaction date's basis (or declare it ambiguous).
    /// </summary>
    private async Task<(
        Dictionary<Guid, List<StockSplit>> SplitsByStock,
        Dictionary<Guid, StockIdentity> IdentityByStock
    )> FetchSplitContext(List<InsiderTransaction> batch)
    {
        var stockIds = batch.Select(t => t.CommonStockId).Distinct().ToList();

        var splitsByStock = (
            await _stockSplitRepository
                .GetEffective(DateOnly.FromDateTime(DateTime.UtcNow))
                .Where(sp => stockIds.Contains(sp.CommonStockId))
                .ToListAsync()
        )
            .GroupBy(sp => sp.CommonStockId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var identityByStock = (
            await _dbContext
                .Set<CommonStock>()
                .Where(cs => stockIds.Contains(cs.Id))
                .Select(cs => new
                {
                    cs.Id,
                    cs.Ticker,
                    cs.SecondaryTickers,
                })
                .ToListAsync()
        ).ToDictionary(cs => cs.Id, cs => new StockIdentity(cs.Ticker, cs.SecondaryTickers ?? []));

        return (splitsByStock, identityByStock);
    }
}
