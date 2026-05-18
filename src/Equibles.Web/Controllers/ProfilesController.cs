using Equibles.Congress.Repositories;
using Equibles.Holdings.Repositories;
using Equibles.InsiderTrading.Repositories;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.Extensions;
using Equibles.Web.ViewModels.Profiles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Controllers;

// Standalone read-only profiles so global-search hits for institutions, insiders and
// congress members are navigable (issue #888) — they previously had no destination.
public class ProfilesController : BaseController
{
    private const int RecentRowLimit = 25;

    private readonly InstitutionalHolderRepository _institutionalHolderRepository;
    private readonly InstitutionalHoldingRepository _institutionalHoldingRepository;
    private readonly InsiderOwnerRepository _insiderOwnerRepository;
    private readonly InsiderTransactionRepository _insiderTransactionRepository;
    private readonly CongressMemberRepository _congressMemberRepository;
    private readonly CongressionalTradeRepository _congressionalTradeRepository;

    public ProfilesController(
        InstitutionalHolderRepository institutionalHolderRepository,
        InstitutionalHoldingRepository institutionalHoldingRepository,
        InsiderOwnerRepository insiderOwnerRepository,
        InsiderTransactionRepository insiderTransactionRepository,
        CongressMemberRepository congressMemberRepository,
        CongressionalTradeRepository congressionalTradeRepository,
        ILogger<ProfilesController> logger
    )
        : base(logger)
    {
        _institutionalHolderRepository = institutionalHolderRepository;
        _institutionalHoldingRepository = institutionalHoldingRepository;
        _insiderOwnerRepository = insiderOwnerRepository;
        _insiderTransactionRepository = insiderTransactionRepository;
        _congressMemberRepository = congressMemberRepository;
        _congressionalTradeRepository = congressionalTradeRepository;
    }

    [HttpGet("~/Institutions/{cik}")]
    public async Task<IActionResult> Institution(string cik)
    {
        var holder = await _institutionalHolderRepository.GetByCik(cik);
        if (holder == null)
            return NotFound();

        var holdings = await _institutionalHoldingRepository
            .GetHistoryByHolder(holder)
            .OrderByDescending(holding => holding.ReportDate)
            .Take(RecentRowLimit)
            .Select(holding => new HoldingRowViewModel
            {
                Ticker = holding.CommonStock.Ticker,
                Company = holding.CommonStock.Name,
                ReportDate = holding.ReportDate,
                Shares = holding.Shares,
                Value = holding.Value,
            })
            .ToListAsync();

        ViewData["Title"] = holder.Name;
        return View(
            new InstitutionProfileViewModel
            {
                Name = holder.Name,
                Cik = holder.Cik,
                Location = ProfileFormatting.JoinLocation(holder.City, holder.StateOrCountry),
                Holdings = holdings,
            }
        );
    }

    [HttpGet("~/Insiders/{ownerCik}")]
    public async Task<IActionResult> Insider(string ownerCik)
    {
        var owner = await _insiderOwnerRepository.GetByOwnerCik(ownerCik);
        if (owner == null)
            return NotFound();

        var transactions = await _insiderTransactionRepository
            .GetByOwner(owner)
            .OrderByDescending(transaction => transaction.TransactionDate)
            .Take(RecentRowLimit)
            .Select(transaction => new InsiderTradeRowViewModel
            {
                Ticker = transaction.CommonStock.Ticker,
                TransactionDate = transaction.TransactionDate,
                SecurityTitle = transaction.SecurityTitle,
                Shares = transaction.Shares,
                PricePerShare = transaction.PricePerShare,
            })
            .ToListAsync();

        ViewData["Title"] = owner.Name;
        return View(
            new InsiderProfileViewModel
            {
                Name = owner.Name,
                OwnerCik = owner.OwnerCik,
                Location = ProfileFormatting.JoinLocation(owner.City, owner.StateOrCountry),
                Role = ProfileFormatting.DescribeRole(
                    owner.OfficerTitle,
                    owner.IsDirector,
                    owner.IsTenPercentOwner
                ),
                Transactions = transactions,
            }
        );
    }

    [HttpGet("~/Congress/{id:guid}")]
    public async Task<IActionResult> Member(Guid id)
    {
        var member = await _congressMemberRepository.Get(id);
        if (member == null)
            return NotFound();

        var trades = await _congressionalTradeRepository
            .GetByMember(member)
            .OrderByDescending(trade => trade.TransactionDate)
            .Take(RecentRowLimit)
            .Select(trade => new CongressTradeRowViewModel
            {
                Ticker = trade.CommonStock.Ticker,
                TransactionDate = trade.TransactionDate,
                AssetName = trade.AssetName,
                OwnerType = trade.OwnerType,
                AmountFrom = trade.AmountFrom,
                AmountTo = trade.AmountTo,
            })
            .ToListAsync();

        ViewData["Title"] = member.Name;
        return View(new CongressProfileViewModel { Name = member.Name, Trades = trades });
    }
}
