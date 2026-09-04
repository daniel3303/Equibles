using Equibles.CommonStocks.Data.Helpers;
using Equibles.Congress.Repositories;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Equibles.InsiderTrading.Data.Extensions;
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

    // The fund-score variant shown on the profile: the default rolling window and benchmark the
    // scoring worker writes.
    private const int ScoreWindowYears = FundScoringManager.DefaultWindowYears;
    private const string ScoreBenchmark = FundScoringManager.DefaultBenchmark;

    private readonly InstitutionalHolderRepository _institutionalHolderRepository;
    private readonly InstitutionalHoldingRepository _institutionalHoldingRepository;
    private readonly FundScoreRepository _fundScoreRepository;
    private readonly InsiderOwnerRepository _insiderOwnerRepository;
    private readonly InsiderTransactionRepository _insiderTransactionRepository;
    private readonly CongressMemberRepository _congressMemberRepository;
    private readonly CongressMemberRedirectRepository _congressMemberRedirectRepository;
    private readonly CongressionalTradeRepository _congressionalTradeRepository;
    private readonly HoldingsBacktestService _holdingsBacktestService;
    private readonly InstitutionPortfolioSummaryProvider _institutionPortfolioSummaryProvider;

    public ProfilesController(
        InstitutionalHolderRepository institutionalHolderRepository,
        InstitutionalHoldingRepository institutionalHoldingRepository,
        FundScoreRepository fundScoreRepository,
        InsiderOwnerRepository insiderOwnerRepository,
        InsiderTransactionRepository insiderTransactionRepository,
        CongressMemberRepository congressMemberRepository,
        CongressMemberRedirectRepository congressMemberRedirectRepository,
        CongressionalTradeRepository congressionalTradeRepository,
        HoldingsBacktestService holdingsBacktestService,
        InstitutionPortfolioSummaryProvider institutionPortfolioSummaryProvider,
        ILogger<ProfilesController> logger
    )
        : base(logger)
    {
        _institutionalHolderRepository = institutionalHolderRepository;
        _institutionalHoldingRepository = institutionalHoldingRepository;
        _fundScoreRepository = fundScoreRepository;
        _insiderOwnerRepository = insiderOwnerRepository;
        _insiderTransactionRepository = insiderTransactionRepository;
        _congressMemberRepository = congressMemberRepository;
        _congressMemberRedirectRepository = congressMemberRedirectRepository;
        _congressionalTradeRepository = congressionalTradeRepository;
        _holdingsBacktestService = holdingsBacktestService;
        _institutionPortfolioSummaryProvider = institutionPortfolioSummaryProvider;
    }

    [HttpGet("~/institutions/{cik}")]
    public async Task<IActionResult> Institution(string cik, DateOnly? activityDate = null)
    {
        var validatedCik = CikNormalizer.Validate(cik);
        if (validatedCik == null)
            return NotFound();

        var holder = await _institutionalHolderRepository.GetByCik(validatedCik);
        if (holder == null)
            return NotFound();

        var holdings = await _institutionalHoldingRepository
            .Get13FHistoryByHolder(holder)
            .TakeMostRecent(holding => holding.ReportDate, RecentRowLimit)
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
        var distinctDates =
            await _institutionalHoldingRepository.Get13FReportDatesByHolderSnapshotBacked(holder);
        var (summary, industryAllocation) = await BuildSummaryAndAllocation(holder, distinctDates);
        var (quarterlyActivity, activityResolved, activityPrior) = await BuildQuarterlyActivity(
            holder,
            distinctDates,
            activityDate
        );

        var fundScore = await _fundScoreRepository.GetByHolder(
            holder,
            ScoreWindowYears,
            ScoreBenchmark
        );

        ViewData["Title"] = holder.Name;
        return View(
            new InstitutionProfileViewModel
            {
                Name = holder.Name,
                Cik = holder.Cik,
                Classification = holder.Classification,
                ConfidentialTreatmentRequested = holder.ConfidentialTreatmentRequested,
                Location = ProfileFormatting.JoinLocation(holder.City, holder.StateOrCountry),
                Holdings = holdings,
                FundScore = fundScore,
                Summary = summary,
                IndustryAllocation = industryAllocation,
                AvailableReportDates = distinctDates,
                ActivityDate = activityResolved,
                ActivityPriorDate = activityPrior,
                QuarterlyActivity = quarterlyActivity,
            }
        );
    }

    [HttpGet("~/institutions/{cik}/backtest")]
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

        var selectedIndex = requestedDate.HasValue
            ? Math.Max(0, distinctReportDates.ToList().IndexOf(requestedDate.Value))
            : 0;
        var selected = distinctReportDates[selectedIndex];
        if (selectedIndex >= distinctReportDates.Count - 1)
            return (
                new Dictionary<StockPositionChangeType, List<StockPositionChange>>(),
                selected,
                null
            );

        var prior = distinctReportDates[selectedIndex + 1];
        var currentHoldings = await LoadHoldingsByHolderWithStock(holder, selected);
        var previousHoldings = await LoadHoldingsByHolderWithStock(holder, prior);

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
        var summary = await _institutionPortfolioSummaryProvider.Get(
            holder,
            latest,
            previous,
            distinctReportDates.Count
        );

        // Industry allocation still needs the stock/industry navigation, but the summary above
        // reads grouped scalar projections and no longer materializes current + prior holdings.
        var currentHoldingsWithIndustry = await _institutionalHoldingRepository
            .Get13FByHolder(holder, latest)
            .Include(h => h.CommonStock)
                .ThenInclude(s => s.Industry)
            .ToListAsync();
        var allocation = IndustryAllocationCalculator.Calculate(currentHoldingsWithIndustry);
        return (summary, allocation);
    }

    [HttpGet("~/institutions/compare")]
    public async Task<IActionResult> CompareInstitutions(
        [FromQuery(Name = "ciks")] string[] ciks = null,
        [FromQuery(Name = "date")] DateOnly? date = null
    )
    {
        var distinctCiks = NormalizeCiks(ciks);
        if (distinctCiks == null)
            return BadRequest(
                "Every CIK must contain only ASCII digits and be at most 16 characters."
            );
        if (distinctCiks.Count > InstitutionCompareViewModel.MaxCiks)
            return BadRequest(
                $"At most {InstitutionCompareViewModel.MaxCiks} CIKs may be compared."
            );

        var viewModel = new InstitutionCompareViewModel { RequestedCiks = distinctCiks };
        ViewData["Title"] = "Compare institutions";

        viewModel.Overlap = await LoadMultiFundOverlapInto(
            viewModel,
            distinctCiks,
            date,
            InstitutionCompareViewModel.MinCiks
        );

        viewModel.InitialPicks = await ResolvePicks(distinctCiks);
        return View(viewModel);
    }

    [HttpGet("~/institutions/overlap-matrix")]
    public async Task<IActionResult> OverlapMatrix(
        [FromQuery(Name = "ciks")] string[] ciks = null,
        [FromQuery(Name = "date")] DateOnly? date = null
    )
    {
        // The picker submits one ?ciks=A&ciks=B per chip — the default ASP.NET
        // string[] binding shape. The Split is kept for the legacy comma/space
        // shape (?ciks=A,B) that bookmarks and InstitutionsOverlapMatrixSeededTests
        // still use.
        var splitCiks = (ciks ?? [])
            .SelectMany(c => c.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries))
            .ToArray();
        var distinctCiks = NormalizeCiks(splitCiks);
        if (distinctCiks == null)
            return BadRequest(
                "Every CIK must contain only ASCII digits and be at most 16 characters."
            );
        if (distinctCiks.Count > InstitutionOverlapMatrixViewModel.MaxCiks)
            return BadRequest(
                $"At most {InstitutionOverlapMatrixViewModel.MaxCiks} CIKs may be compared."
            );

        var viewModel = new InstitutionOverlapMatrixViewModel { RequestedCiks = distinctCiks };
        ViewData["Title"] = "Overlap matrix";

        var matrixOverlap = await LoadMultiFundOverlapInto(
            viewModel,
            distinctCiks,
            date,
            InstitutionOverlapMatrixViewModel.MinCiks
        );
        if (matrixOverlap != null)
            viewModel.Matrix = FundOverlapCalculator.ComputePairwiseOverlap(matrixOverlap);

        viewModel.InitialPicks = await ResolvePicks(distinctCiks);
        return View(viewModel);
    }

    [HttpGet("~/institutions/combined")]
    public async Task<IActionResult> CombinedInstitutions(
        [FromQuery(Name = "ciks")] string[] ciks = null,
        [FromQuery(Name = "date")] DateOnly? date = null
    )
    {
        var distinctCiks = NormalizeCiks(ciks);
        if (distinctCiks == null)
            return BadRequest(
                "Every CIK must contain only ASCII digits and be at most 16 characters."
            );
        if (distinctCiks.Count > InstitutionCombinedViewModel.MaxCiks)
            return BadRequest(
                $"At most {InstitutionCombinedViewModel.MaxCiks} CIKs may be combined."
            );

        var viewModel = new InstitutionCombinedViewModel { RequestedCiks = distinctCiks };
        ViewData["Title"] = "Combined portfolio";

        var combinedOverlap = await LoadMultiFundOverlapInto(
            viewModel,
            distinctCiks,
            date,
            InstitutionCombinedViewModel.MinCiks
        );
        if (combinedOverlap != null)
        {
            viewModel.Overlap = combinedOverlap;

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
        }

        viewModel.InitialPicks = await ResolvePicks(distinctCiks);
        return View(viewModel);
    }

    [HttpGet("~/insiders/{ownerCik}")]
    public async Task<IActionResult> Insider(string ownerCik)
    {
        var validatedCik = CikNormalizer.Validate(ownerCik);
        if (validatedCik == null)
            return NotFound();

        var owner = await _insiderOwnerRepository.GetByOwnerCik(validatedCik);
        if (owner == null)
            return NotFound();

        var transactions = await _insiderTransactionRepository
            .GetByOwner(owner)
            .ExcludeHoldings()
            .TakeMostRecent(transaction => transaction.TransactionDate, RecentRowLimit)
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

    [HttpGet("~/congress/{id:guid}")]
    public async Task<IActionResult> Member(Guid id)
    {
        var member = await _congressMemberRepository.Get(id);
        if (member == null)
        {
            var mergedIntoId = await _congressMemberRedirectRepository
                .GetByRetiredId(id)
                .Select(redirect => (Guid?)redirect.MergedIntoId)
                .SingleOrDefaultAsync();
            return mergedIntoId.HasValue
                ? RedirectToActionPermanent(nameof(Member), new { id = mergedIntoId.Value })
                : NotFound();
        }

        var trades = await _congressionalTradeRepository
            .GetByMember(member)
            .TakeMostRecent(trade => trade.TransactionDate, RecentRowLimit)
            .Select(trade => new CongressTradeRowViewModel
            {
                Ticker = trade.FiledTicker != ""
                    ? trade.FiledTicker
                    : trade.CommonStock != null ? trade.CommonStock.Ticker : null,
                TransactionDate = trade.TransactionDate,
                AssetName = trade.AssetName,
                AssetType = trade.AssetType,
                OwnerType = trade.OwnerType,
                Subholding = trade.Subholding,
                AmountFrom = trade.AmountFrom,
                AmountTo = trade.AmountTo,
            })
            .ToListAsync();

        ViewData["Title"] = member.Name;
        return View(new CongressProfileViewModel { Name = member.Name, Trades = trades });
    }

    // Loads the shared multi-fund overlap into the common view-model fields and returns
    // the overlap result for each action's view-specific handling; null when fewer than
    // minCiks CIKs were requested (the caller then leaves its overlap/matrix unset).
    private async Task<FundOverlapResult> LoadMultiFundOverlapInto(
        MultiInstitutionViewModel viewModel,
        List<string> distinctCiks,
        DateOnly? date,
        int minCiks
    )
    {
        if (distinctCiks.Count < minCiks)
            return null;

        var loaded = await TryLoadMultiFundOverlap(
            distinctCiks,
            viewModel.MissingCiks,
            date,
            minCiks
        );
        viewModel.CommonReportDates = loaded.CommonReportDates;
        viewModel.SelectedDate = loaded.SelectedDate;
        return loaded.Overlap;
    }

    private async Task<(
        List<DateOnly> CommonReportDates,
        DateOnly? SelectedDate,
        FundOverlapResult Overlap
    )> TryLoadMultiFundOverlap(
        List<string> distinctCiks,
        List<string> missingCikSink,
        DateOnly? date,
        int minCiks
    )
    {
        var holders = await LoadHoldersByCik(distinctCiks, missingCikSink);
        if (holders.Count < minCiks)
            return ([], null, null);

        var commonDates = await FindCommonReportDates(holders);
        if (commonDates.Count == 0)
            return (commonDates, null, null);

        var selected = ResolveSelectedDate(date, commonDates);
        var perFund = await LoadPerFundHoldings(holders, selected);
        var overlap = FundOverlapCalculator.Calculate(perFund, selected);

        return (commonDates, selected, overlap);
    }

    private async Task<List<InstitutionalHolder>> LoadHoldersByCik(
        List<string> distinctCiks,
        List<string> missingCikSink
    )
    {
        var candidates = await _institutionalHolderRepository.GetByCiks(distinctCiks);
        var holders = new List<InstitutionalHolder>();
        var seenHolderIds = new HashSet<Guid>();
        foreach (var cik in distinctCiks)
        {
            var holder = InstitutionalHolderRepository.MatchRequestedCik(candidates, cik);
            if (holder == null)
                missingCikSink.Add(cik);
            else if (seenHolderIds.Add(holder.Id))
                holders.Add(holder);
        }
        return holders;
    }

    // Common report dates = intersection of each holder's distinct report dates.
    // First pass collects per-holder sorted distinct dates; intersection happens in
    // memory so we keep the descending order from the first holder.
    private async Task<List<DateOnly>> FindCommonReportDates(List<InstitutionalHolder> holders)
    {
        var perHolderDates = new List<List<DateOnly>>();
        foreach (var holder in holders)
        {
            var dates =
                await _institutionalHoldingRepository.Get13FReportDatesByHolderSnapshotBacked(
                    holder
                );
            perHolderDates.Add(dates);
        }
        return perHolderDates
            .Skip(1)
            .Aggregate((IEnumerable<DateOnly>)perHolderDates[0], (acc, next) => acc.Intersect(next))
            .OrderByDescending(d => d)
            .ToList();
    }

    // Pull each fund's holdings for the selected date (one query per fund — the
    // funds-side cardinality is bounded by InstitutionCompareViewModel.MaxCiks).
    private async Task<
        List<(InstitutionalHolder Holder, IReadOnlyList<InstitutionalHolding> Holdings)>
    > LoadPerFundHoldings(List<InstitutionalHolder> holders, DateOnly selected)
    {
        var perFund =
            new List<(InstitutionalHolder Holder, IReadOnlyList<InstitutionalHolding> Holdings)>();
        foreach (var holder in holders)
        {
            var holdings = await LoadHoldingsByHolderWithStock(holder, selected);
            perFund.Add((holder, holdings));
        }
        return perFund;
    }

    private Task<List<InstitutionalHolding>> LoadHoldingsByHolderWithStock(
        InstitutionalHolder holder,
        DateOnly reportDate
    ) => _institutionalHoldingRepository.GetByHolderWithStock(holder, reportDate).ToListAsync();

    private static DateOnly ResolveSelectedDate(DateOnly? requested, List<DateOnly> available) =>
        available.ResolveSelectedDateOrFirst(requested);

    private static List<string> NormalizeCiks(string[] ciks)
    {
        var rawCiks = (ciks ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        var validatedCiks = rawCiks.Select(CikNormalizer.Validate).ToList();
        if (validatedCiks.Any(c => c == null))
            return null;

        var normalized = new List<string>(validatedCiks.Count);
        var seenCanonicalCiks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cik in validatedCiks)
        {
            if (seenCanonicalCiks.Add(CikNormalizer.Canonicalize(cik)))
                normalized.Add(cik);
        }

        return normalized;
    }

    // Resolves the picker chips for first render — preserves the order the user
    // submitted valid numeric CIKs in, and leaves Name == null for CIKs that aren't
    // in the DB so the view can flag them as missing.
    private async Task<List<InstitutionPick>> ResolvePicks(List<string> ciks)
    {
        if (ciks.Count == 0)
            return [];

        var holders = await _institutionalHolderRepository.GetByCiks(ciks);

        return ciks.Select(cik => new InstitutionPick
            {
                Cik = cik,
                Name = InstitutionalHolderRepository.MatchRequestedCik(holders, cik)?.Name,
            })
            .ToList();
    }
}
