using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.Extensions;
using Equibles.Web.ViewModels.Institutions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Controllers;

public class InstitutionsController : BaseController
{
    // The fund-score variant surfaced in the list: the same default rolling window and benchmark
    // the scoring worker writes, so the alpha column and sort read the rows it produces.
    private const int ScoreWindowYears = FundScoringManager.DefaultWindowYears;
    private const string ScoreBenchmark = FundScoringManager.DefaultBenchmark;

    private readonly InstitutionalHolderRepository _holderRepository;
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly SmartMoneyIndexManager _smartMoneyIndexManager;

    public InstitutionsController(
        InstitutionalHolderRepository holderRepository,
        EquiblesFinancialDbContext dbContext,
        SmartMoneyIndexManager smartMoneyIndexManager,
        ILogger<InstitutionsController> logger
    )
        : base(logger)
    {
        _holderRepository = holderRepository;
        _dbContext = dbContext;
        _smartMoneyIndexManager = smartMoneyIndexManager;
    }

    [HttpGet("~/institutions")]
    public async Task<IActionResult> Index(
        string search,
        string state,
        string city,
        long? minValue,
        long? maxValue,
        int? minPositions,
        int? maxPositions,
        InstitutionSort sort = InstitutionSort.Name,
        int page = 1
    )
    {
        ViewData["Title"] = "Institutions";

        page = Pagination.ClampPage(page);

        const int pageSize = 50;

        var latestReportDate = await _dbContext
            .Set<InstitutionalHolding>()
            .Select(h => (DateOnly?)h.ReportDate)
            .MaxAsync();

        // Per-filer aggregate at each filer's OWN most-recent 13F report. The
        // universe-latest approach drops historical filers (e.g. Scion's 2022
        // book never shows up once the universe ticks past 2022), so the table
        // would mis-report active book sizes as zero. The two-stage shape (max
        // date per holder + join + group) translates to a single Postgres
        // query with a self-join.
        var perFilerLatest = _dbContext
            .Set<InstitutionalHolding>()
            .GroupBy(h => h.InstitutionalHolderId)
            .Select(g => new { HolderId = g.Key, LatestDate = g.Max(h => h.ReportDate) });

        var aggByHolder = _dbContext
            .Set<InstitutionalHolding>()
            .Join(
                perFilerLatest,
                h => new { Holder = h.InstitutionalHolderId, Date = h.ReportDate },
                l => new { Holder = l.HolderId, Date = l.LatestDate },
                (h, _) => h
            )
            .GroupBy(h => h.InstitutionalHolderId)
            .Select(g => new
            {
                HolderId = g.Key,
                Positions = g.Count(),
                Value = g.Sum(h => h.Value),
                LatestDate = g.Max(h => h.ReportDate),
            });

        var holders = _holderRepository.Search(search ?? string.Empty);

        // Location filters narrow the filer set before the aggregate join. State
        // is an exact match on a dropdown value; city is a case-insensitive
        // substring so "new" matches "New York".
        if (!string.IsNullOrWhiteSpace(state))
        {
            holders = holders.Where(h => h.StateOrCountry == state);
        }
        if (!string.IsNullOrWhiteSpace(city))
        {
            holders = holders.Where(h => EF.Functions.ILike(h.City, $"%{city.Trim()}%"));
        }

        var joined = holders.GroupJoin(
            aggByHolder,
            h => h.Id,
            a => a.HolderId,
            (h, aggs) => new { h, agg = aggs.FirstOrDefault() }
        );

        // AUM ($ value) and position-count range filters apply to each filer's
        // most-recent 13F aggregate. A filer with no holdings is treated as zero
        // on both axes, so any positive lower bound excludes never-reported filers.
        if (minValue.HasValue)
        {
            joined = joined.Where(x => (x.agg != null ? x.agg.Value : 0L) >= minValue.Value);
        }
        if (maxValue.HasValue)
        {
            joined = joined.Where(x => (x.agg != null ? x.agg.Value : 0L) <= maxValue.Value);
        }
        if (minPositions.HasValue)
        {
            joined = joined.Where(x => (x.agg != null ? x.agg.Positions : 0) >= minPositions.Value);
        }
        if (maxPositions.HasValue)
        {
            joined = joined.Where(x => (x.agg != null ? x.agg.Positions : 0) <= maxPositions.Value);
        }

        // Each filer's latest alpha for the surfaced window/benchmark, as a correlated scalar
        // subquery. The unique index makes it 1:0..1; a missing score reads as null. Kept as a
        // subquery (not a second GroupJoin) so it composes onto the aggregate join above without
        // an untranslatable join-of-a-join.
        var scoresQuery = _dbContext
            .Set<FundScore>()
            .Where(s => s.WindowYears == ScoreWindowYears && s.BenchmarkTicker == ScoreBenchmark);

        var ordered = sort switch
        {
            InstitutionSort.PositionsDescending => joined
                .OrderByDescending(x => x.agg != null ? x.agg.Positions : 0)
                .ThenBy(x => x.h.Name)
                .ThenBy(x => x.h.Id),
            InstitutionSort.ValueDescending => joined
                .OrderByDescending(x => x.agg != null ? x.agg.Value : 0L)
                .ThenBy(x => x.h.Name)
                .ThenBy(x => x.h.Id),
            // Scored filers first (alpha highest to lowest), unscored filers last: a missing score
            // coalesces to the smallest value so it sinks to the bottom of a descending sort.
            InstitutionSort.AlphaDescending => joined
                .OrderByDescending(x =>
                    scoresQuery
                        .Where(s => s.InstitutionalHolderId == x.h.Id)
                        .Select(s => (decimal?)s.AlphaPercent)
                        .FirstOrDefault()
                    ?? decimal.MinValue
                )
                .ThenBy(x => x.h.Name)
                .ThenBy(x => x.h.Id),
            _ => joined.OrderBy(x => x.h.Name).ThenBy(x => x.h.Id),
        };

        var totalCount = await ordered.CountAsync();

        var pageRows = await ordered
            .Page(page, pageSize)
            .Select(x => new InstitutionListItemViewModel
            {
                Id = x.h.Id,
                Cik = x.h.Cik,
                Name = x.h.Name,
                City = x.h.City,
                StateOrCountry = x.h.StateOrCountry,
                PositionCount = x.agg != null ? x.agg.Positions : 0,
                TotalValue = x.agg != null ? x.agg.Value : 0L,
                LatestReportDate = x.agg != null ? (DateOnly?)x.agg.LatestDate : null,
                AlphaPercent = scoresQuery
                    .Where(s => s.InstitutionalHolderId == x.h.Id)
                    .Select(s => (decimal?)s.AlphaPercent)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        var states = await _holderRepository
            .DistinctStatesOrCountries()
            .OrderBy(s => s)
            .ToListAsync();

        var viewModel = new InstitutionBrowserViewModel
        {
            Institutions = pageRows,
            Search = search,
            State = state,
            City = city,
            States = states,
            MinValue = minValue,
            MaxValue = maxValue,
            MinPositions = minPositions,
            MaxPositions = maxPositions,
            Sort = sort,
            LatestReportDate = latestReportDate,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };

        return View(viewModel);
    }

    // Smart-money index: the equal-weighted basket of the top-scoring funds' highest-conviction
    // common holdings, with its forward performance tracked against the benchmark. Query params
    // tune the construction; each is clamped so hand-edited URLs can't push the build off-range.
    [HttpGet("~/institutions/smart-money-index")]
    public async Task<IActionResult> SmartMoneyIndex(
        int topFunds = SmartMoneyIndexCalculator.DefaultTopFunds,
        int maxConstituents = SmartMoneyIndexCalculator.DefaultMaxConstituents,
        int minConsensus = SmartMoneyIndexCalculator.DefaultMinConsensus
    )
    {
        ViewData["Title"] = "Smart Money Index";

        topFunds = Math.Clamp(topFunds, 1, 100);
        maxConstituents = Math.Clamp(maxConstituents, 1, 100);
        minConsensus = Math.Clamp(minConsensus, 1, topFunds);

        var result = await _smartMoneyIndexManager.Build(
            DateOnly.FromDateTime(DateTime.UtcNow),
            topFunds,
            maxConstituents,
            minConsensus,
            ScoreWindowYears,
            ScoreBenchmark
        );

        return View(result);
    }

    // JSON typeahead endpoint backing the institution picker (overlap/compare/combined
    // pages). Returns a small top-N projection — no aggregates, just enough to render
    // a chip with name + city/state hint.
    [HttpGet("~/institutions/search")]
    public async Task<IActionResult> Search(string q, int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return Json(Array.Empty<object>());
        }
        if (limit < 1)
            limit = 1;
        if (limit > 50)
            limit = 50;

        // Lowercase property names pin the wire contract — the JS picker reads
        // row.cik/row.name/etc. without depending on the host's JSON casing policy.
        var rows = await _holderRepository
            .SearchNameOrCik(q.Trim())
            .OrderBy(h => h.Name)
            .Take(limit)
            .Select(h => new
            {
                cik = h.Cik,
                name = h.Name,
                city = h.City,
                stateOrCountry = h.StateOrCountry,
            })
            .ToListAsync();

        return Json(rows);
    }
}
