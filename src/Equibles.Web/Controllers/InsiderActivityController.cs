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

        var topBuys = await _transactionRepository
            .GetRecentByType(TransactionCode.Purchase, since)
            .OrderByDescending(t => t.Shares * t.PricePerShare)
            .Take(InsiderDashboardViewModel.RowCap)
            .Select(t => new InsiderDashboardRow
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
            })
            .ToListAsync();

        var topSells = await _transactionRepository
            .GetRecentByType(TransactionCode.Sale, since)
            .OrderByDescending(t => t.Shares * t.PricePerShare)
            .Take(InsiderDashboardViewModel.RowCap)
            .Select(t => new InsiderDashboardRow
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
            })
            .ToListAsync();

        var biggest = await _transactionRepository
            .GetRecent(since)
            .OrderByDescending(t => t.Shares * t.PricePerShare)
            .Take(InsiderDashboardViewModel.RowCap)
            .Select(t => new InsiderDashboardRow
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
            })
            .ToListAsync();

        return View(
            new InsiderDashboardViewModel
            {
                TopBuys = topBuys,
                TopSells = topSells,
                BiggestTransactions = biggest,
            }
        );
    }
}
