using Equibles.Congress.Repositories;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.Extensions;
using Equibles.Web.Services;
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
    private readonly HoldingsBacktestService _holdingsBacktestService;

    public ProfilesController(
        InstitutionalHolderRepository institutionalHolderRepository,
        InstitutionalHoldingRepository institutionalHoldingRepository,
        InsiderOwnerRepository insiderOwnerRepository,
        InsiderTransactionRepository insiderTransactionRepository,
        CongressMemberRepository congressMemberRepository,
        CongressionalTradeRepository congressionalTradeRepository,
        HoldingsBacktestService holdingsBacktestService,
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
        _holdingsBacktestService = holdingsBacktestService;
    }

    [HttpGet("~/Institutions/{cik}")]
    public async Task<IActionResult> Institution(string cik, DateOnly? activityDate = null)
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

        // Header strip + industry allocation — pulled with extra per-quarter
        // materializations so the existing recent-rows list keeps its top-50 shape.
        var distinctDates = await _institutionalHoldingRepository
            .GetHistoryByHolder(holder)
            .Select(h => h.ReportDate)
            .Distinct()
            .OrderByDescending(d => d)
            .ToListAsync();
        var (summary, industryAllocation) = await BuildSummaryAndAllocation(holder, distinctDates);
        var (quarterlyActivity, activityResolved, activityPrior) = await BuildQuarterlyActivity(
            holder,
            distinctDates,
            activityDate
        );

        ViewData["Title"] = holder.Name;
        return View(
            new InstitutionProfileViewModel
            {
                Name = holder.Name,
                Cik = holder.Cik,
                Location = ProfileFormatting.JoinLocation(holder.City, holder.StateOrCountry),
                Holdings = holdings,
                Summary = summary,
                IndustryAllocation = industryAllocation,
                AvailableReportDates = distinctDates,
                ActivityDate = activityResolved,
                ActivityPriorDate = activityPrior,
                QuarterlyActivity = quarterlyActivity,
            }
        );
    }

    [HttpGet("~/Institutions/{cik}/Backtest")]
    public async Task<IActionResult> BacktestInstitution(
        string cik,
        [FromQuery(Name = "from")] DateOnly? from = null,
        [FromQuery(Name = "to")] DateOnly? to = null,
        [FromQuery(Name = "benchmark")] string benchmark = null
    )
    {
        var viewModel = await _holdingsBacktestService.Execute(cik, from, to, benchmark);
        if (viewModel.HolderNotFound)
            return NotFound();

        ViewData["Title"] = viewModel.HolderName + " · Backtest";
        return View(viewModel);
    }

    private async Task<(
        Dictionary<StockPositionChangeType, List<StockPositionChange>> Buckets,
        DateOnly? Selected,
        DateOnly? Prior
    )> BuildQuarterlyActivity(
        InstitutionalHolder holder,
        IReadOnlyList<DateOnly> distinctReportDates,
        DateOnly? requestedDate
    )
    {
        if (distinctReportDates.Count < 2)
            return (
                new Dictionary<StockPositionChangeType, List<StockPositionChange>>(),
                distinctReportDates.Count == 1 ? distinctReportDates[0] : (DateOnly?)null,
                null
            );

        DateOnly selected;
        var selectedIndex = -1;
        if (requestedDate.HasValue)
        {
            for (var i = 0; i < distinctReportDates.Count; i++)
            {
                if (distinctReportDates[i] != requestedDate.Value)
                    continue;
                selectedIndex = i;
                break;
            }
        }
        if (selectedIndex < 0)
        {
            selected = distinctReportDates[0];
            selectedIndex = 0;
        }
        else
        {
            selected = distinctReportDates[selectedIndex];
        }
        if (selectedIndex >= distinctReportDates.Count - 1)
            return (
                new Dictionary<StockPositionChangeType, List<StockPositionChange>>(),
                selected,
                null
            );

        var prior = distinctReportDates[selectedIndex + 1];
        var currentHoldings = await _institutionalHoldingRepository
            .GetByHolder(holder, selected)
            .Include(h => h.CommonStock)
            .ToListAsync();
        var previousHoldings = await _institutionalHoldingRepository
            .GetByHolder(holder, prior)
            .Include(h => h.CommonStock)
            .ToListAsync();

        var grouped = HolderQuarterlyActivityCalculator.Group(currentHoldings, previousHoldings);

        // Cap each bucket and pre-sort by |Δ value| desc so the view renders the
        // largest movers first per section.
        var capped = grouped.ToDictionary(
            kv => kv.Key,
            kv =>
                kv.Value.OrderByDescending(r => Math.Abs(r.DeltaValue))
                    .Take(InstitutionProfileViewModel.ActivityRowCap)
                    .ToList()
        );

        return (capped, selected, prior);
    }

    private async Task<(
        InstitutionPortfolioSummary Summary,
        List<IndustryAllocationSlice> Allocation
    )> BuildSummaryAndAllocation(
        InstitutionalHolder holder,
        IReadOnlyList<DateOnly> distinctReportDates
    )
    {
        if (distinctReportDates.Count == 0)
            return (new InstitutionPortfolioSummary { QuartersReported = 0 }, []);

        var latest = distinctReportDates[0];
        var previous = distinctReportDates.Count > 1 ? distinctReportDates[1] : (DateOnly?)null;
        // Current quarter loaded twice — shallow for the summary calculator and again with
        // the Industry navigation for the allocation calculator. Kept separate so the
        // summary path doesn't pay the Industry join cost.
        var currentHoldings = await _institutionalHoldingRepository
            .GetByHolder(holder, latest)
            .ToListAsync();
        var currentHoldingsWithIndustry = await _institutionalHoldingRepository
            .GetByHolder(holder, latest)
            .Include(h => h.CommonStock)
                .ThenInclude(s => s.Industry)
            .ToListAsync();
        var previousHoldings = previous.HasValue
            ? await _institutionalHoldingRepository
                .GetByHolder(holder, previous.Value)
                .ToListAsync()
            : [];

        var summary = InstitutionPortfolioSummaryCalculator.Calculate(
            currentHoldings,
            previousHoldings,
            distinctReportDates.Count,
            latest,
            previous
        );
        var allocation = IndustryAllocationCalculator.Calculate(currentHoldingsWithIndustry);
        return (summary, allocation);
    }

    [HttpGet("~/Institutions/Compare")]
    public async Task<IActionResult> CompareInstitutions(
        [FromQuery(Name = "ciks")] string[] ciks = null,
        [FromQuery(Name = "date")] DateOnly? date = null
    )
    {
        ciks ??= [];
        var distinctCiks = ciks.Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinctCiks.Count > InstitutionCompareViewModel.MaxCiks)
            return BadRequest(
                $"At most {InstitutionCompareViewModel.MaxCiks} CIKs may be compared."
            );

        var viewModel = new InstitutionCompareViewModel { RequestedCiks = distinctCiks };
        ViewData["Title"] = "Compare institutions";

        if (distinctCiks.Count < InstitutionCompareViewModel.MinCiks)
            return View(viewModel);

        var holders = new List<InstitutionalHolder>();
        foreach (var cik in distinctCiks)
        {
            var holder = await _institutionalHolderRepository.GetByCik(cik);
            if (holder == null)
                viewModel.MissingCiks.Add(cik);
            else
                holders.Add(holder);
        }
        if (holders.Count < InstitutionCompareViewModel.MinCiks)
            return View(viewModel);

        // Common report dates = intersection of each holder's distinct report dates.
        // First pass collects per-holder sorted distinct dates; intersection happens in
        // memory so we keep the descending order from the first holder.
        var perHolderDates = new List<List<DateOnly>>();
        foreach (var holder in holders)
        {
            var dates = await _institutionalHoldingRepository
                .GetHistoryByHolder(holder)
                .Select(h => h.ReportDate)
                .Distinct()
                .OrderByDescending(d => d)
                .ToListAsync();
            perHolderDates.Add(dates);
        }
        var commonDates = perHolderDates
            .Skip(1)
            .Aggregate((IEnumerable<DateOnly>)perHolderDates[0], (acc, next) => acc.Intersect(next))
            .OrderByDescending(d => d)
            .ToList();
        viewModel.CommonReportDates = commonDates;
        if (commonDates.Count == 0)
            return View(viewModel);

        var selected = date ?? commonDates[0];
        if (!commonDates.Contains(selected))
            selected = commonDates[0];
        viewModel.SelectedDate = selected;

        // Pull each fund's holdings for the selected date (one query per fund — the
        // funds-side cardinality is bounded to 4).
        var perFund =
            new List<(InstitutionalHolder Holder, IReadOnlyList<InstitutionalHolding> Holdings)>();
        foreach (var holder in holders)
        {
            var holdings = await _institutionalHoldingRepository
                .GetByHolder(holder, selected)
                .Include(h => h.CommonStock)
                .ToListAsync();
            perFund.Add((holder, holdings));
        }
        viewModel.Overlap = FundOverlapCalculator.Calculate(perFund, selected);
        return View(viewModel);
    }

    [HttpGet("~/Institutions/Combined")]
    public async Task<IActionResult> CombinedInstitutions(
        [FromQuery(Name = "ciks")] string[] ciks = null,
        [FromQuery(Name = "date")] DateOnly? date = null
    )
    {
        ciks ??= [];
        var distinctCiks = ciks.Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinctCiks.Count > InstitutionCombinedViewModel.MaxCiks)
            return BadRequest(
                $"At most {InstitutionCombinedViewModel.MaxCiks} CIKs may be combined."
            );

        var viewModel = new InstitutionCombinedViewModel { RequestedCiks = distinctCiks };
        ViewData["Title"] = "Combined portfolio";

        if (distinctCiks.Count < InstitutionCombinedViewModel.MinCiks)
            return View(viewModel);

        var holders = new List<InstitutionalHolder>();
        foreach (var cik in distinctCiks)
        {
            var holder = await _institutionalHolderRepository.GetByCik(cik);
            if (holder == null)
                viewModel.MissingCiks.Add(cik);
            else
                holders.Add(holder);
        }
        if (holders.Count < InstitutionCombinedViewModel.MinCiks)
            return View(viewModel);

        var perHolderDates = new List<List<DateOnly>>();
        foreach (var holder in holders)
        {
            var dates = await _institutionalHoldingRepository
                .GetHistoryByHolder(holder)
                .Select(h => h.ReportDate)
                .Distinct()
                .OrderByDescending(d => d)
                .ToListAsync();
            perHolderDates.Add(dates);
        }
        var commonDates = perHolderDates
            .Skip(1)
            .Aggregate((IEnumerable<DateOnly>)perHolderDates[0], (acc, next) => acc.Intersect(next))
            .OrderByDescending(d => d)
            .ToList();
        viewModel.CommonReportDates = commonDates;
        if (commonDates.Count == 0)
            return View(viewModel);

        var selected = date ?? commonDates[0];
        if (!commonDates.Contains(selected))
            selected = commonDates[0];
        viewModel.SelectedDate = selected;

        var perFund =
            new List<(InstitutionalHolder Holder, IReadOnlyList<InstitutionalHolding> Holdings)>();
        foreach (var holder in holders)
        {
            var holdings = await _institutionalHoldingRepository
                .GetByHolder(holder, selected)
                .Include(h => h.CommonStock)
                .ToListAsync();
            perFund.Add((holder, holdings));
        }
        viewModel.Overlap = FundOverlapCalculator.Calculate(perFund, selected);

        // Consensus count per stock = number of funds whose slice has Value > 0. This is
        // the primary sort key for the combined-portfolio view; the calculator already
        // sorts by combined value, so we apply consensus on top as a stable secondary
        // re-sort.
        foreach (var row in viewModel.Overlap.Rows)
        {
            var heldBy = row.Slices.Count(s => s.Value > 0);
            viewModel.FundsHoldingByStock[row.CommonStockId] = heldBy;
        }
        viewModel.Overlap.Rows = viewModel
            .Overlap.Rows.OrderByDescending(r => viewModel.FundsHoldingByStock[r.CommonStockId])
            .ThenByDescending(r => r.CombinedValue)
            .ToList();

        return View(viewModel);
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
