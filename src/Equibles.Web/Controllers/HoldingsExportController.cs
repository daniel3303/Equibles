using System.Text;
using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.Data.Models;
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

    [HttpGet("~/holdings/export/holders")]
    public async Task<IActionResult> Holders(string ticker, DateOnly? date)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            return NotFound();

        var stock = await _stockRepository.GetByTicker(ticker.ToUpperInvariant());
        if (stock == null)
            return NotFound();

        var reportDates = await LoadDistinctReportDates(
            _holdingRepository.GetHistoryByStock(stock)
        );
        if (reportDates.Count == 0)
            return NotFound();

        var selectedDate = ResolveSelectedDate(date, reportDates);

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
        return CsvFile(csv, $"{stock.Ticker}-13F-{selectedDate:yyyy-MM-dd}.csv");
    }

    [HttpGet("~/holdings/export/institution")]
    public async Task<IActionResult> Institution(string cik, DateOnly? date)
    {
        if (string.IsNullOrWhiteSpace(cik))
            return NotFound();

        var holder = await _holderRepository.GetByCik(cik);
        if (holder == null)
            return NotFound();

        var reportDates = await LoadDistinctReportDates(
            _holdingRepository.GetHistoryByHolder(holder)
        );
        if (reportDates.Count == 0)
            return NotFound();

        var selectedDate = ResolveSelectedDate(date, reportDates);

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
        return CsvFile(csv, $"{Sanitize(holder.Cik)}-portfolio-{selectedDate:yyyy-MM-dd}.csv");
    }

    [HttpGet("~/holdings/export/activity")]
    public async Task<IActionResult> Activity(DateOnly? date)
    {
        var reportDates = await _holdingRepository
            .GetAvailableReportDates()
            .OrderByDescending(d => d)
            .ToListAsync();
        if (reportDates.Count < 2)
            return NotFound();

        var selectedDate = ResolveSelectedDate(date, reportDates);
        var selectedIndex = reportDates.IndexOf(selectedDate);
        if (selectedIndex >= reportDates.Count - 1)
            return NotFound();
        var previousDate = reportDates[selectedIndex + 1];

        // Per-stock buy/sell movers (CSV has no row cap — analysts expect the full set).
        var activity = await _holdingRepository
            .GetQuarterlyActivity(selectedDate, previousDate)
            .Where(a => a.CurrentShares != a.PreviousShares)
            .ToListAsync();
        var topBuys = activity
            .Where(a => a.CurrentShares > a.PreviousShares)
            .OrderByDescending(a => a.CurrentValue - a.PreviousValue)
            .ToList();
        var topSells = activity
            .Where(a => a.CurrentShares < a.PreviousShares)
            .OrderBy(a => a.CurrentValue - a.PreviousValue)
            .ToList();

        var churn = await _holdingRepository
            .GetQuarterlyNewSoldOutPositions(selectedDate, previousDate)
            .Where(c => c.NewFilerCount > 0 || c.SoldOutFilerCount > 0)
            .ToListAsync();
        var newPositions = churn
            .Where(c => c.NewFilerCount > 0)
            .OrderByDescending(c => c.NewFilerCount)
            .ToList();
        var soldOut = churn
            .Where(c => c.SoldOutFilerCount > 0)
            .OrderByDescending(c => c.SoldOutFilerCount)
            .ToList();

        var stockIds = topBuys
            .Concat(topSells)
            .Select(a => a.CommonStockId)
            .Concat(newPositions.Concat(soldOut).Select(c => c.CommonStockId))
            .Distinct()
            .ToList();
        var stocks = await _stockRepository
            .GetAll()
            .Where(s => stockIds.Contains(s.Id))
            .Select(s => new StockLabel(s.Id, s.Ticker, s.Name))
            .ToDictionaryAsync(s => s.Id);

        string[] headers =
        [
            "Board",
            "ReportDate",
            "ComparisonDate",
            "Ticker",
            "CompanyName",
            "CurrentFilerCount",
            "PreviousFilerCount",
            "DeltaShares",
            "DeltaValue",
            "NewFilerCount",
            "SoldOutFilerCount",
        ];

        var rows = new List<string[]>();
        foreach (var row in topBuys)
            rows.Add(ActivityRow("TopBuys", row, selectedDate, previousDate, stocks));
        foreach (var row in topSells)
            rows.Add(ActivityRow("TopSells", row, selectedDate, previousDate, stocks));
        foreach (var row in newPositions)
            rows.Add(ChurnRow("NewPositions", row, selectedDate, previousDate, stocks));
        foreach (var row in soldOut)
            rows.Add(ChurnRow("SoldOutPositions", row, selectedDate, previousDate, stocks));

        var csv = CsvExportService.BuildCsv(headers, rows);
        return CsvFile(csv, $"13F-activity-{selectedDate:yyyy-MM-dd}.csv");
    }

    private FileContentResult CsvFile(string csv, string filename)
    {
        Response.Headers.CacheControl = "no-store";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", filename);
    }

    private static Task<List<DateOnly>> LoadDistinctReportDates(
        IQueryable<InstitutionalHolding> source
    ) => source.Select(h => h.ReportDate).Distinct().OrderByDescending(d => d).ToListAsync();

    private static DateOnly ResolveSelectedDate(DateOnly? requested, List<DateOnly> available) =>
        requested.HasValue && available.Contains(requested.Value) ? requested.Value : available[0];

    private static string[] ActivityRow(
        string board,
        Equibles.Holdings.Repositories.Models.MarketWideStockActivity row,
        DateOnly current,
        DateOnly previous,
        IDictionary<Guid, StockLabel> stocks
    )
    {
        stocks.TryGetValue(row.CommonStockId, out var stock);
        return
        [
            board,
            CsvExportService.Format(current),
            CsvExportService.Format(previous),
            stock?.Ticker ?? string.Empty,
            stock?.Name ?? string.Empty,
            CsvExportService.Format((long)row.CurrentFilerCount),
            CsvExportService.Format((long)row.PreviousFilerCount),
            CsvExportService.Format(row.CurrentShares - row.PreviousShares),
            CsvExportService.Format(row.CurrentValue - row.PreviousValue),
            string.Empty,
            string.Empty,
        ];
    }

    private static string[] ChurnRow(
        string board,
        Equibles.Holdings.Repositories.Models.MarketWideStockChurn row,
        DateOnly current,
        DateOnly previous,
        IDictionary<Guid, StockLabel> stocks
    )
    {
        stocks.TryGetValue(row.CommonStockId, out var stock);
        return
        [
            board,
            CsvExportService.Format(current),
            CsvExportService.Format(previous),
            stock?.Ticker ?? string.Empty,
            stock?.Name ?? string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            CsvExportService.Format((long)row.NewFilerCount),
            CsvExportService.Format((long)row.SoldOutFilerCount),
        ];
    }

    private record StockLabel(Guid Id, string Ticker, string Name);

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
