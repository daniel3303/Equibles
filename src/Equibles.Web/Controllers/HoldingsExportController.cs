using System.Text;
using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.Repositories;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Controllers;

public class HoldingsExportController : BaseController
{
    private readonly CommonStockRepository _stockRepository;
    private readonly InstitutionalHoldingRepository _holdingRepository;

    public HoldingsExportController(
        CommonStockRepository stockRepository,
        InstitutionalHoldingRepository holdingRepository,
        ILogger<HoldingsExportController> logger
    )
        : base(logger)
    {
        _stockRepository = stockRepository;
        _holdingRepository = holdingRepository;
    }

    [HttpGet("~/Holdings/Export/Holders")]
    public async Task<IActionResult> Holders(string ticker, DateOnly? date)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            return NotFound();

        var stock = await _stockRepository.GetByTicker(ticker.ToUpperInvariant());
        if (stock == null)
            return NotFound();

        var reportDates = await _holdingRepository
            .GetHistoryByStock(stock)
            .Select(h => h.ReportDate)
            .Distinct()
            .OrderByDescending(d => d)
            .ToListAsync();
        if (reportDates.Count == 0)
            return NotFound();

        var selectedDate =
            date.HasValue && reportDates.Contains(date.Value) ? date.Value : reportDates[0];

        var holdings = await _holdingRepository
            .GetByStock(stock, selectedDate)
            .Include(h => h.InstitutionalHolder)
            .OrderByDescending(h => h.Value)
            .Select(h => new
            {
                HolderName = h.InstitutionalHolder.Name,
                HolderCik = h.InstitutionalHolder.Cik,
                h.Shares,
                h.Value,
                h.ShareType,
                h.OptionType,
                h.AccessionNumber,
            })
            .ToListAsync();

        string[] headers =
        [
            "Ticker",
            "CompanyName",
            "ReportDate",
            "InstitutionalHolderName",
            "InstitutionalHolderCik",
            "Shares",
            "Value",
            "ShareType",
            "OptionType",
            "AccessionNumber",
        ];

        var rows = holdings.Select(h =>
            new[]
            {
                stock.Ticker,
                stock.Name,
                CsvExportService.Format(selectedDate),
                h.HolderName,
                h.HolderCik,
                CsvExportService.Format(h.Shares),
                CsvExportService.Format(h.Value),
                h.ShareType.ToString(),
                h.OptionType?.ToString() ?? string.Empty,
                h.AccessionNumber,
            }
        );

        var csv = CsvExportService.BuildCsv(headers, rows);
        var filename = $"{stock.Ticker}-13F-{selectedDate:yyyy-MM-dd}.csv";
        Response.Headers.CacheControl = "no-store";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", filename);
    }
}
