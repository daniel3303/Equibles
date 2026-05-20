using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Core.Extensions;
using Equibles.Finra.Repositories;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.InsiderTrading.Repositories;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Equibles.Web.ViewModels.Stocks;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Services;

[Service]
public class StockTabService
{
    private readonly InstitutionalHoldingRepository _institutionalHoldingRepository;
    private readonly InstitutionalHolderRepository _institutionalHolderRepository;
    private readonly DailyShortVolumeRepository _dailyShortVolumeRepository;
    private readonly ShortInterestRepository _shortInterestRepository;
    private readonly FailToDeliverRepository _failToDeliverRepository;
    private readonly DocumentRepository _documentRepository;
    private readonly InsiderTransactionRepository _insiderTransactionRepository;
    private readonly CongressionalTradeRepository _congressionalTradeRepository;
    private readonly DailyStockPriceRepository _dailyStockPriceRepository;
    private readonly FinancialFactRepository _financialFactRepository;
    private readonly FinancialConceptRepository _financialConceptRepository;

    public StockTabService(
        InstitutionalHoldingRepository institutionalHoldingRepository,
        InstitutionalHolderRepository institutionalHolderRepository,
        DailyShortVolumeRepository dailyShortVolumeRepository,
        ShortInterestRepository shortInterestRepository,
        FailToDeliverRepository failToDeliverRepository,
        DocumentRepository documentRepository,
        InsiderTransactionRepository insiderTransactionRepository,
        CongressionalTradeRepository congressionalTradeRepository,
        DailyStockPriceRepository dailyStockPriceRepository,
        FinancialFactRepository financialFactRepository,
        FinancialConceptRepository financialConceptRepository
    )
    {
        _institutionalHoldingRepository = institutionalHoldingRepository;
        _institutionalHolderRepository = institutionalHolderRepository;
        _dailyShortVolumeRepository = dailyShortVolumeRepository;
        _shortInterestRepository = shortInterestRepository;
        _failToDeliverRepository = failToDeliverRepository;
        _documentRepository = documentRepository;
        _insiderTransactionRepository = insiderTransactionRepository;
        _congressionalTradeRepository = congressionalTradeRepository;
        _dailyStockPriceRepository = dailyStockPriceRepository;
        _financialFactRepository = financialFactRepository;
        _financialConceptRepository = financialConceptRepository;
    }

    public async Task<HoldingsTabViewModel> LoadHoldingsTab(CommonStock stock, DateOnly? date)
    {
        var reportDates = await _institutionalHoldingRepository
            .GetHistoryByStock(stock)
            .Select(h => h.ReportDate)
            .Distinct()
            .OrderByDescending(d => d)
            .ToListAsync();

        var selectedDate = date ?? reportDates.FirstOrDefault();

        if (selectedDate == default)
        {
            return new HoldingsTabViewModel
            {
                AvailableDates = reportDates,
                SelectedDate = selectedDate,
                Ticker = stock.Ticker,
            };
        }

        // Materialize the full current quarter once: drives header stats AND the
        // position-change grouping. Previously this was two round trips (sum/count
        // aggregates + a top-100 fetch); the bucketed view needs every holder anyway,
        // so the in-memory aggregates are cheaper than re-querying.
        var allCurrent = await _institutionalHoldingRepository
            .GetByStock(stock, selectedDate)
            .Include(h => h.InstitutionalHolder)
            .ToListAsync();

        var selectedIndex = reportDates.IndexOf(selectedDate);
        var previousDate =
            selectedIndex >= 0 && selectedIndex < reportDates.Count - 1
                ? reportDates[selectedIndex + 1]
                : (DateOnly?)null;
        var allPrevious = previousDate.HasValue
            ? await _institutionalHoldingRepository
                .GetByStock(stock, previousDate.Value)
                .Include(h => h.InstitutionalHolder)
                .ToListAsync()
            : [];

        var grouped = HoldingsPositionGrouper.Group(allCurrent, allPrevious);
        var bucketCounts = grouped.ToDictionary(g => g.Key, g => g.Value.Count);
        var (topBuyers, topSellers) = HoldingsTopMoversSelector.Select(
            grouped,
            HoldingsTabViewModel.TopMoversPreviewCount
        );

        return new HoldingsTabViewModel
        {
            AvailableDates = reportDates,
            SelectedDate = selectedDate,
            Ticker = stock.Ticker,
            TotalValue = allCurrent.Sum(h => h.Value),
            TotalShares = allCurrent.Sum(h => h.Shares),
            HolderCount = allCurrent.Select(h => h.InstitutionalHolderId).Distinct().Count(),
            GroupedHolders = grouped,
            BucketCounts = bucketCounts,
            TopBuyers = topBuyers,
            TopSellers = topSellers,
            TotalBuyerCount = HoldingsTopMoversSelector.CountBuyers(grouped),
            TotalSellerCount = HoldingsTopMoversSelector.CountSellers(grouped),
        };
    }

    public async Task<ShortVolumeTabViewModel> LoadShortVolumeTab(CommonStock stock)
    {
        var shortVolumes = await _dailyShortVolumeRepository
            .GetHistoryByStock(stock)
            .OrderByDescending(d => d.Date)
            .Take(90)
            .ToListAsync();
        return new ShortVolumeTabViewModel
        {
            ShortVolumes = shortVolumes.OrderBy(d => d.Date).ToList(),
            Ticker = stock.Ticker,
        };
    }

    public async Task<ShortInterestTabViewModel> LoadShortInterestTab(CommonStock stock)
    {
        var shortInterests = await _shortInterestRepository
            .GetHistoryByStock(stock)
            .OrderByDescending(s => s.SettlementDate)
            .Take(24)
            .ToListAsync();
        return new ShortInterestTabViewModel
        {
            ShortInterests = shortInterests.OrderBy(s => s.SettlementDate).ToList(),
            Ticker = stock.Ticker,
        };
    }

    public async Task<FtdTabViewModel> LoadFtdTab(CommonStock stock)
    {
        var ftds = await _failToDeliverRepository
            .GetByStock(stock)
            .OrderByDescending(f => f.SettlementDate)
            .Take(90)
            .ToListAsync();
        return new FtdTabViewModel
        {
            FailsToDeliver = ftds.OrderBy(f => f.SettlementDate).ToList(),
            Ticker = stock.Ticker,
        };
    }

    public async Task<DocumentsTabViewModel> LoadDocumentsTab(CommonStock stock)
    {
        var documents = await _documentRepository
            .GetByCompany(stock)
            .OrderByDescending(d => d.ReportingDate)
            .Take(100)
            .ToListAsync();
        return new DocumentsTabViewModel { Documents = documents, Ticker = stock.Ticker };
    }

    public async Task<InsiderTradingTabViewModel> LoadInsiderTradingTab(CommonStock stock)
    {
        var transactions = await _insiderTransactionRepository
            .GetByStock(stock)
            .Include(t => t.InsiderOwner)
            .OrderByDescending(t => t.TransactionDate)
            .Take(100)
            .ToListAsync();
        return new InsiderTradingTabViewModel
        {
            Transactions = transactions,
            Ticker = stock.Ticker,
        };
    }

    public async Task<CongressionalTradesTabViewModel> LoadCongressionalTradesTab(CommonStock stock)
    {
        var trades = await _congressionalTradeRepository
            .GetByStock(stock)
            .Include(t => t.CongressMember)
            .OrderByDescending(t => t.TransactionDate)
            .Take(100)
            .ToListAsync();
        return new CongressionalTradesTabViewModel { Trades = trades, Ticker = stock.Ticker };
    }

    public async Task<PriceTabViewModel> LoadPriceTab(CommonStock stock)
    {
        var prices = await _dailyStockPriceRepository
            .GetByStock(stock)
            .OrderBy(p => p.Date)
            .ToListAsync();

        var closePrices = prices.Select(p => p.Close).ToList();
        var (macdLine, macdSignal, macdHistogram) = TechnicalIndicatorService.ComputeMacd(
            closePrices
        );

        return new PriceTabViewModel
        {
            Ticker = stock.Ticker,
            Prices = prices,
            Sma20 = TechnicalIndicatorService.ComputeSma(closePrices, 20),
            Sma50 = TechnicalIndicatorService.ComputeSma(closePrices, 50),
            Sma200 = TechnicalIndicatorService.ComputeSma(closePrices, 200),
            Rsi14 = TechnicalIndicatorService.ComputeRsi(closePrices),
            MacdLine = macdLine,
            MacdSignal = macdSignal,
            MacdHistogram = macdHistogram,
        };
    }

    public async Task<FinancialsTabViewModel> LoadFinancialsTab(
        CommonStock stock,
        FinancialStatementType statementType,
        int? year,
        SecFiscalPeriod? period
    )
    {
        // Distinct (year, period) pairs the company actually reported. This is a
        // separate round trip from the per-period fact query below — both are
        // covered by the [CommonStockId, FiscalYear, FiscalPeriod] index, and
        // keeping them separate avoids loading every fact just to list periods.
        var periodKeys = await _financialFactRepository
            .GetByStock(stock)
            .Select(f => new { f.FiscalYear, f.FiscalPeriod })
            .Distinct()
            .ToListAsync();

        // SecFiscalPeriod's enum ordinal (FullYear=0, Q1=1…Q4=4) is not
        // chronological — ordering by it would float the annual figure to the
        // wrong end. The 10-K (FullYear) is filed after Q4 and is the canonical
        // annual number, so rank it last within its year; default selection
        // (first option) is then the latest year's annual statement.
        var availablePeriods = periodKeys
            .OrderByDescending(p => p.FiscalYear)
            .ThenByDescending(p => ChronologicalRank(p.FiscalPeriod))
            .Select(p => new FinancialsPeriodOption(
                p.FiscalYear,
                p.FiscalPeriod,
                $"FY{p.FiscalYear} {p.FiscalPeriod.NameForHumans()}"
            ))
            .ToList();

        var viewModel = new FinancialsTabViewModel
        {
            Ticker = stock.Ticker,
            StatementType = statementType,
            AvailablePeriods = availablePeriods,
        };

        if (availablePeriods.Count == 0)
            return viewModel;

        // Default to the most recent period; fall back to it when the requested
        // (year, period) is not one the company actually reported.
        var selected =
            availablePeriods.FirstOrDefault(p => p.FiscalYear == year && p.FiscalPeriod == period)
            ?? availablePeriods[0];
        viewModel.SelectedYear = selected.FiscalYear;
        viewModel.SelectedPeriod = selected.FiscalPeriod;

        var statementLines = FinancialStatementConcepts.For(statementType);
        var taxonomies = statementLines.Select(l => l.Taxonomy).Distinct().ToList();
        var tags = statementLines.Select(l => l.Tag).Distinct().ToList();

        var concepts = await _financialConceptRepository
            .GetMatching(taxonomies, tags)
            .Select(c => new
            {
                c.Id,
                c.Taxonomy,
                c.Tag,
            })
            .ToListAsync();
        var conceptIdByKey = concepts.ToDictionary(c => (c.Taxonomy, c.Tag), c => c.Id);
        var conceptIds = concepts.Select(c => c.Id).ToHashSet();

        var facts = await _financialFactRepository
            .GetByStock(stock)
            .Where(f =>
                f.FiscalYear == selected.FiscalYear
                && f.FiscalPeriod == selected.FiscalPeriod
                && conceptIds.Contains(f.FinancialConceptId)
            )
            .ToListAsync();

        // SEC re-emits a concept across filings (restatements); the latest-filed
        // value is the currently-reported one.
        var latestByConcept = facts
            .GroupBy(f => f.FinancialConceptId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.FiledDate).First());

        viewModel.Lines = statementLines
            .Select(line =>
            {
                var row = new FinancialsLineViewModel { Label = line.Label };
                if (
                    conceptIdByKey.TryGetValue((line.Taxonomy, line.Tag), out var conceptId)
                    && latestByConcept.TryGetValue(conceptId, out var fact)
                )
                {
                    row.HasValue = true;
                    row.Value = fact.Value;
                    row.Unit = fact.Unit;
                    row.PeriodEnd = fact.PeriodEnd;
                    row.Form = fact.Form?.DisplayName;
                    row.FiledDate = fact.FiledDate;
                }
                return row;
            })
            .ToList();

        return viewModel;
    }

    // Chronological order within a fiscal year: Q1 < Q2 < Q3 < Q4 < FullYear.
    private static int ChronologicalRank(SecFiscalPeriod period) =>
        period switch
        {
            SecFiscalPeriod.Q1 => 1,
            SecFiscalPeriod.Q2 => 2,
            SecFiscalPeriod.Q3 => 3,
            SecFiscalPeriod.Q4 => 4,
            SecFiscalPeriod.FullYear => 5,
            _ => 0,
        };

    public async Task<HolderDetailViewModel> LoadHolderDetail(
        CommonStock stock,
        InstitutionalHolder holder
    )
    {
        var holdings = await _institutionalHoldingRepository
            .GetHistoryByStock(stock)
            .Where(h => h.InstitutionalHolderId == holder.Id)
            .OrderByDescending(h => h.ReportDate)
            .Take(50)
            .ToListAsync();

        return new HolderDetailViewModel
        {
            Stock = stock,
            Holder = holder,
            Holdings = holdings,
        };
    }
}
