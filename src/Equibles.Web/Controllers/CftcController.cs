using Equibles.Cftc.Data.Models;
using Equibles.Cftc.Repositories;
using Equibles.Core.Extensions;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.ViewModels.Cftc;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Controllers;

public class CftcController : BaseController {
    private readonly CftcContractRepository _contractRepository;
    private readonly CftcPositionReportRepository _reportRepository;

    public CftcController(
        CftcContractRepository contractRepository,
        CftcPositionReportRepository reportRepository,
        ILogger<CftcController> logger
    ) : base(logger) {
        _contractRepository = contractRepository;
        _reportRepository = reportRepository;
    }

    [HttpGet("~/Futures")]
    public async Task<IActionResult> Index() {
        var allContracts = await _contractRepository.GetAll()
            .OrderBy(c => c.Category)
            .ThenBy(c => c.MarketName)
            .ToListAsync();

        var latestByContractId = await _reportRepository.GetLatestPerContract()
            .ToDictionaryAsync(r => r.CftcContractId, r => r);

        var categories = allContracts
            .GroupBy(c => c.Category)
            .Select(g => new CftcCategoryGroup {
                Category = g.Key,
                DisplayName = g.Key.NameForHumans(),
                Contracts = g.Select(c => {
                    latestByContractId.TryGetValue(c.Id, out var latest);
                    return new CftcContractItem {
                        MarketCode = c.MarketCode,
                        MarketName = c.MarketName,
                        CommercialNet = latest != null ? latest.CommLong - latest.CommShort : null,
                        NonCommercialNet = latest != null ? latest.NonCommLong - latest.NonCommShort : null,
                        LatestDate = latest?.ReportDate
                    };
                }).ToList()
            })
            .ToList();

        return View(new CftcIndexViewModel { Categories = categories });
    }

    [HttpGet("~/Futures/{marketCode}")]
    public async Task<IActionResult> Show(string marketCode) {
        if (string.IsNullOrWhiteSpace(marketCode)) return NotFound();

        var contract = await _contractRepository.GetByMarketCode(marketCode.Trim()).FirstOrDefaultAsync();
        if (contract == null) return NotFound();

        var reports = await _reportRepository.GetByContract(contract)
            .OrderByDescending(r => r.ReportDate)
            .Select(r => new CftcReportItem {
                ReportDate = r.ReportDate,
                OpenInterest = r.OpenInterest,
                CommLong = r.CommLong,
                CommShort = r.CommShort,
                NonCommLong = r.NonCommLong,
                NonCommShort = r.NonCommShort,
                NonCommSpreads = r.NonCommSpreads,
                ChangeOpenInterest = r.ChangeOpenInterest
            })
            .ToListAsync();

        var latest = reports.FirstOrDefault();

        var viewModel = new CftcContractViewModel {
            MarketCode = contract.MarketCode,
            MarketName = contract.MarketName,
            Category = contract.Category,
            CategoryDisplayName = contract.Category.NameForHumans(),
            Reports = reports,
            LatestOpenInterest = latest?.OpenInterest,
            LatestCommercialNet = latest != null ? latest.CommLong - latest.CommShort : null,
            LatestNonCommercialNet = latest != null ? latest.NonCommLong - latest.NonCommShort : null,
            LatestNonCommSpreads = latest?.NonCommSpreads
        };

        ViewData["Title"] = $"{contract.MarketCode} — {contract.MarketName}";
        ViewData["Description"] = $"COT positioning data for {contract.MarketName} ({contract.MarketCode}).";
        return View(viewModel);
    }
}
