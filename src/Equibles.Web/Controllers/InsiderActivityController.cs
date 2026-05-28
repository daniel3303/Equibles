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
            }
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
            }
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
            }
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

    // Top N priced transactions by dollar value (Shares * PricePerShare) from the given
    // source query, projected via the caller's expression. EF translates the projection
    // server-side, so the SELECT shape per call matches the previous inline chain.
    private static Task<List<TRow>> LoadTopRows<TRow>(
        IQueryable<InsiderTransaction> source,
        Expression<Func<InsiderTransaction, TRow>> projection
    ) =>
        source
            .Where(t => t.IsPriceValid)
            .OrderByDescending(t => t.Shares * t.PricePerShare)
            .Take(InsiderDashboardViewModel.RowCap)
            .Select(projection)
            .ToListAsync();
}
