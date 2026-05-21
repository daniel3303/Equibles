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
    private readonly EquiblesDbContext _dbContext;

    public InstitutionsController(
        InstitutionalHolderRepository holderRepository,
        EquiblesDbContext dbContext,
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

        // Per-filer aggregate at the universe-latest report date. EF Core
        // translates this to a single GROUP BY pushed to Postgres; the
        // GroupJoin below preserves filers that didn't report in the latest
        // quarter (their aggregate row is null → rendered as 0).
        var aggByHolder = _dbContext
            .Set<InstitutionalHolding>()
            .Where(h => latestReportDate != null && h.ReportDate == latestReportDate.Value)
            .GroupBy(h => h.InstitutionalHolderId)
            .Select(g => new
            {
                HolderId = g.Key,
                Positions = g.Count(),
                Value = g.Sum(h => h.Value),
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
