using Equibles.Sec.Repositories;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.Extensions;
using Equibles.Web.ViewModels.Advisers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Controllers;

public class AdvisersController : BaseController
{
    private const int PageSize = 50;

    private readonly FormAdvAdviserRepository _adviserRepository;

    public AdvisersController(
        FormAdvAdviserRepository adviserRepository,
        ILogger<AdvisersController> logger
    )
        : base(logger)
    {
        _adviserRepository = adviserRepository;
    }

    [HttpGet("~/advisers")]
    public async Task<IActionResult> Index(string q, int page = 1)
    {
        ViewData["Title"] = "Investment Advisers";
        ViewData["Description"] =
            "Browse SEC-registered investment advisers from Form ADV — assets under management, location and fee structure.";

        page = Pagination.ClampPage(page);

        var query = string.IsNullOrWhiteSpace(q)
            ? _adviserRepository.GetLargestByAum()
            : _adviserRepository.Search(q);

        var totalCount = await query.CountAsync();
        var advisers = await query.Page(page, PageSize).ToListAsync();

        return View(
            new AdvisersIndexViewModel
            {
                Query = q,
                Advisers = advisers,
                Page = page,
                TotalPages = Pagination.PageCount(totalCount, PageSize),
                TotalCount = totalCount,
            }
        );
    }

    [HttpGet("~/advisers/{crd:int}")]
    public async Task<IActionResult> Show(int crd)
    {
        var adviser = await _adviserRepository.GetByCrd(crd).FirstOrDefaultAsync();
        if (adviser == null)
        {
            return NotFound();
        }

        ViewData["Title"] = adviser.LegalName ?? $"Adviser CRD {adviser.Crd}";
        ViewData["Description"] =
            $"Form ADV profile for {ViewData["Title"]} — assets under management, location and fee structure.";
        return View(adviser);
    }
}
