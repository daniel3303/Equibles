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
    private readonly InstitutionalHolderRepository _holderRepository;

    public HoldingsExportController(
        CommonStockRepository stockRepository,
        InstitutionalHoldingRepository holdingRepository,
        InstitutionalHolderRepository holderRepository,
        ILogger<HoldingsExportController> logger
    )
        : base(logger)
    {
        _stockRepository = stockRepository;
        _holdingRepository = holdingRepository;
        _holderRepository = holderRepository;
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

    [HttpGet("~/Holdings/Export/Institution")]
    public async Task<IActionResult> Institution(string cik, DateOnly? date)
    {
        if (string.IsNullOrWhiteSpace(cik))
            return NotFound();

        var holder = await _holderRepository.GetByCik(cik);
        if (holder == null)
            return NotFound();

        var reportDates = await _holdingRepository
            .GetHistoryByHolder(holder)
            .Select(h => h.ReportDate)
            .Distinct()
            .OrderByDescending(d => d)
            .ToListAsync();
        if (reportDates.Count == 0)
            return NotFound();

        var selectedDate =
            date.HasValue && reportDates.Contains(date.Value) ? date.Value : reportDates[0];

        var rowsRaw = await _holdingRepository
            .GetByHolder(holder, selectedDate)
            .Include(h => h.CommonStock)
            .OrderByDescending(h => h.Value)
            .Select(h => new
            {
                Ticker = h.CommonStock.Ticker,
                Name = h.CommonStock.Name,
                h.Shares,
                h.Value,
                h.ShareType,
                h.OptionType,
                h.AccessionNumber,
            })
            .ToListAsync();

        string[] headers =
        [
            "InstitutionalHolderName",
            "InstitutionalHolderCik",
            "ReportDate",
            "Ticker",
            "CompanyName",
            "Shares",
            "Value",
            "ShareType",
            "OptionType",
            "AccessionNumber",
        ];

        var rows = rowsRaw.Select(r =>
            new[]
            {
                holder.Name,
                holder.Cik,
                CsvExportService.Format(selectedDate),
                r.Ticker,
                r.Name,
                CsvExportService.Format(r.Shares),
                CsvExportService.Format(r.Value),
                r.ShareType.ToString(),
                r.OptionType?.ToString() ?? string.Empty,
                r.AccessionNumber,
            }
        );

        var csv = CsvExportService.BuildCsv(headers, rows);
        var filename = $"{Sanitize(holder.Cik)}-portfolio-{selectedDate:yyyy-MM-dd}.csv";
        Response.Headers.CacheControl = "no-store";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", filename);
    }

    // CIKs are numeric strings in production, but the URL could carry a hand-typed value.
    // Strip anything that's unsafe in a filename (slashes / quotes / control chars) so the
    // Content-Disposition header stays well-formed.
    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "institution";
        var safe = new string(
            value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray()
        );
        return string.IsNullOrEmpty(safe) ? "institution" : safe;
    }
}
