using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.ViewModels.Institutions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Controllers;

public class InstitutionsController : BaseController
{
    private readonly InstitutionalHolderRepository _holderRepository;
    private readonly EquiblesFinancialDbContext _dbContext;

    public InstitutionsController(
        InstitutionalHolderRepository holderRepository,
        EquiblesFinancialDbContext dbContext,
        ILogger<InstitutionsController> logger
    )
        : base(logger)
    {
        _holderRepository = holderRepository;
        _dbContext = dbContext;
    }

    [HttpGet("~/institutions")]
    public async Task<IActionResult> Index(
        string search,
        InstitutionSort sort = InstitutionSort.Name,
        int page = 1
    )
    {
        ViewData["Title"] = "Institutions";

        // page is a client-supplied query value; a non-positive page would emit
        // Skip((page-1)*pageSize) = a negative OFFSET, which PostgreSQL rejects.
        if (page < 1)
            page = 1;

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
        var joined = holders.GroupJoin(
            aggByHolder,
            h => h.Id,
            a => a.HolderId,
            (h, ags) => new { h, agg = ags.FirstOrDefault() }
        );

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
            _ => joined.OrderBy(x => x.h.Name).ThenBy(x => x.h.Id),
        };

        var totalCount = await ordered.CountAsync();

        var pageRows = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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
            })
            .ToListAsync();

        var viewModel = new InstitutionBrowserViewModel
        {
            Institutions = pageRows,
            Search = search,
            Sort = sort,
            LatestReportDate = latestReportDate,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };

        return View(viewModel);
    }
}
