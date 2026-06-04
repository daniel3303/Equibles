using System.Linq.Expressions;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.ViewModels.Insider;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Controllers;

public class InsiderActivityController : BaseController
{
    private readonly InsiderTransactionRepository _transactionRepository;

    public InsiderActivityController(
        InsiderTransactionRepository transactionRepository,
        ILogger<InsiderActivityController> logger
    )
        : base(logger)
    {
        _transactionRepository = transactionRepository;
    }

    [HttpGet("~/insider-trading/dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90));

        // A single block sold by a private-equity sponsor is reported on a
        // separate Form 4 by every entity in its beneficial-ownership chain, so
        // the same economic transaction lands in the data many times with an
        // identical ticker/date/share-count/price/code. Collapse those to one
        // row so the boards aren't dominated by the same block repeated.
        static object DedupKey(InsiderDashboardRow row) =>
            (
                row.Ticker,
                row.TransactionDate,
                row.Shares,
                row.PricePerShare,
                row.TransactionCodeLabel
            );

        var topBuys = await LoadTopRows(
            _transactionRepository.GetRecentByType(TransactionCode.Purchase, since),
            t => new InsiderDashboardRow
            {
                OwnerName = t.InsiderOwner.Name,
                OwnerCik = t.InsiderOwner.OwnerCik,
                Ticker = t.CommonStock.Ticker,
                TransactionDate = t.TransactionDate,
                Shares = t.Shares,
                PricePerShare = t.PricePerShare,
                SecurityTitle = t.SecurityTitle,
                TransactionCodeLabel = "Buy",
                IsAcquisition = true,
            },
            DedupKey
        );

        var topSells = await LoadTopRows(
            _transactionRepository.GetRecentByType(TransactionCode.Sale, since),
            t => new InsiderDashboardRow
            {
                OwnerName = t.InsiderOwner.Name,
                OwnerCik = t.InsiderOwner.OwnerCik,
                Ticker = t.CommonStock.Ticker,
                TransactionDate = t.TransactionDate,
                Shares = t.Shares,
                PricePerShare = t.PricePerShare,
                SecurityTitle = t.SecurityTitle,
                TransactionCodeLabel = "Sell",
                IsAcquisition = false,
            },
            DedupKey
        );

        var biggest = await LoadTopRows(
            _transactionRepository.GetRecent(since),
            t => new InsiderDashboardRow
            {
                OwnerName = t.InsiderOwner.Name,
                OwnerCik = t.InsiderOwner.OwnerCik,
                Ticker = t.CommonStock.Ticker,
                TransactionDate = t.TransactionDate,
                Shares = t.Shares,
                PricePerShare = t.PricePerShare,
                SecurityTitle = t.SecurityTitle,
                TransactionCodeLabel = t.TransactionCode.ToString(),
                IsAcquisition = t.AcquiredDisposed == AcquiredDisposed.Acquired,
            },
            DedupKey
        );

        return View(
            new InsiderDashboardViewModel
            {
                TopBuys = topBuys,
                TopSells = topSells,
                BiggestTransactions = biggest,
            }
        );
    }

    // Fetch this many times RowCap before de-duplicating, so collapsing a block
    // reported across a long beneficial-ownership chain still leaves enough rows
    // to fill the board.
    private const int DedupFetchMultiplier = 8;

    // Top N priced transactions by dollar value (Shares * PricePerShare) from the given
    // source query, projected via the caller's expression. EF translates the projection
    // server-side, so the SELECT shape per call matches the previous inline chain.
    private static async Task<List<TRow>> LoadTopRows<TRow>(
        IQueryable<InsiderTransaction> source,
        Expression<Func<InsiderTransaction, TRow>> projection,
        Func<TRow, object> dedupKey
    )
    {
        var rows = await source
            // Show valid and not-yet-evaluated (null) rows; hide only rows
            // positively rejected as implausible.
            .Where(t => t.IsPriceValid != false)
            // Exclude derivatives: their PricePerShare is the instrument's own
            // price, so Shares * PricePerShare is not a dollar value and would
            // otherwise dominate the value sort with nonsense (option/warrant/
            // convertible "prices" in the millions).
            .Where(InsiderSecurityClassification.IsShareTransaction)
            .OrderByDescending(t => t.Shares * t.PricePerShare)
            .Take(InsiderDashboardViewModel.RowCap * DedupFetchMultiplier)
            .Select(projection)
            .ToListAsync();

        var seen = new HashSet<object>();
        var deduped = new List<TRow>(InsiderDashboardViewModel.RowCap);
        foreach (var row in rows)
        {
            if (!seen.Add(dedupKey(row)))
                continue;
            deduped.Add(row);
            if (deduped.Count == InsiderDashboardViewModel.RowCap)
                break;
        }

        return deduped;
    }
}
