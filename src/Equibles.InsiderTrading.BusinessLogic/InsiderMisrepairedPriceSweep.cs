using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Equibles.InsiderTrading.BusinessLogic;

/// <summary>
/// Restores rows the old basis-blind validator "repaired": it compared the as-filed price
/// against the split-ADJUSTED close believing it unadjusted, so every pre-split filing looked
/// implausible and got its correct price divided by the share count — 15,822 rows across 257
/// stocks, all self-sealed <c>IsPriceValid = true</c> (AMZN's pre-20:1 $3,300.24 became $8.42).
/// </summary>
/// <remarks>
/// A misrepaired row carries its own signature: the stored price times the share count lands
/// back on the as-filed figure (<c>|PricePerShare × Shares − ReportedPricePerShare| &lt; 0.01</c>)
/// while the two prices differ. The sweep restores <c>PricePerShare = ReportedPricePerShare</c>
/// and nulls <c>IsPriceValid</c> so the fixed both-bases validator re-evaluates through the
/// ordinary pending path. Genuine fat-fingers share the signature — restoring them is correct
/// too: re-evaluation repairs them again, this time stamping
/// <see cref="InsiderTransaction.PriceWasRepaired"/>, which excludes them from every later
/// sweep. Self-terminating: a restored row's prices are equal, so it can never match again.
/// Bounded per cycle; a no-op scan once drained.
/// </remarks>
[Service]
public class InsiderMisrepairedPriceSweep
{
    internal const int MaxRowsPerCycle = 25_000;

    private readonly InsiderTransactionRepository _transactionRepository;
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly ILogger<InsiderMisrepairedPriceSweep> _logger;

    public InsiderMisrepairedPriceSweep(
        InsiderTransactionRepository transactionRepository,
        EquiblesFinancialDbContext dbContext,
        ILogger<InsiderMisrepairedPriceSweep> logger
    )
    {
        _transactionRepository = transactionRepository;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<int> Run(CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.IsRelational())
        {
            _dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        }

        // Only rows the OLD validator sealed: valid, price diverging from reported, and not
        // stamped by the band-guarded repair (every new repair stamps PriceWasRepaired). The
        // decimal product runs server-side so an oversized share count cannot overflow Int64.
        var rows = await _transactionRepository
            .GetAll()
            .Where(t =>
                t.IsPriceValid == true
                && !t.PriceWasRepaired
                && t.ReportedPricePerShare > 0m
                && t.PricePerShare != t.ReportedPricePerShare
                && Math.Abs(t.PricePerShare * t.Shares - t.ReportedPricePerShare) < 0.01m
            )
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.Id)
            .Take(MaxRowsPerCycle)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return 0;
        }

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            row.PricePerShare = row.ReportedPricePerShare;
            row.IsPriceValid = null;
        }

        await _transactionRepository.SaveChanges();

        _logger.LogWarning(
            "Misrepaired-price sweep: restored {Count} row(s) to their as-filed price for "
                + "re-evaluation under the split-basis-aware validator",
            rows.Count
        );

        return rows.Count;
    }
}
