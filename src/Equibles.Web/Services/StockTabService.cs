using System.Linq.Expressions;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Core.Extensions;
using Equibles.Data.Extensions;
using Equibles.Finra.Repositories;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.InsiderTrading.Data.Extensions;
using Equibles.InsiderTrading.Repositories;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Equibles.Web.Extensions;
using Equibles.Web.ViewModels.Stocks;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Web.Services;

[Service]
public class StockTabService
{
    private const int RecentRowLimit = 100;

    private readonly InstitutionalHoldingRepository _institutionalHoldingRepository;
    private readonly InstitutionalHolderRepository _institutionalHolderRepository;
    private readonly DailyShortVolumeRepository _dailyShortVolumeRepository;
    private readonly ShortInterestRepository _shortInterestRepository;
    private readonly FailToDeliverRepository _failToDeliverRepository;
    private readonly DocumentRepository _documentRepository;
    private readonly InsiderTransactionRepository _insiderTransactionRepository;
    private readonly Form144FilingRepository _form144FilingRepository;
    private readonly FormDFilingRepository _formDFilingRepository;
    private readonly NCenFilingRepository _nCenFilingRepository;
    private readonly NportFilingRepository _nportFilingRepository;
    private readonly CongressionalTradeRepository _congressionalTradeRepository;
    private readonly DailyStockPriceRepository _dailyStockPriceRepository;
    private readonly FinancialFactRepository _financialFactRepository;
    private readonly FinancialConceptRepository _financialConceptRepository;
    private readonly CommonStockRepository _commonStockRepository;

    // Data before the configured sync floor is partial — the scrapers only
    // backfill from it — so historical series clamp to it rather than render
    // misleading low/zero readings. Null = no floor configured, no clamp.
    private readonly DateOnly? _minSyncDate;

    // Benchmark the Price tab compares each stock's returns against.
    private const string BenchmarkTicker = "SPY";

    // SPY history loaded back from the latest bar to cover every return window:
    // the 120-trading-day window (~168 calendar days) and the YTD anchor (up to a
    // full prior year). 420 days clears both with margin.
    private const int BenchmarkLookbackDays = 420;

    public StockTabService(
        InstitutionalHoldingRepository institutionalHoldingRepository,
        InstitutionalHolderRepository institutionalHolderRepository,
        DailyShortVolumeRepository dailyShortVolumeRepository,
        ShortInterestRepository shortInterestRepository,
        FailToDeliverRepository failToDeliverRepository,
        DocumentRepository documentRepository,
        InsiderTransactionRepository insiderTransactionRepository,
        Form144FilingRepository form144FilingRepository,
        FormDFilingRepository formDFilingRepository,
        NCenFilingRepository nCenFilingRepository,
        NportFilingRepository nportFilingRepository,
        CongressionalTradeRepository congressionalTradeRepository,
        DailyStockPriceRepository dailyStockPriceRepository,
        FinancialFactRepository financialFactRepository,
        FinancialConceptRepository financialConceptRepository,
        CommonStockRepository commonStockRepository,
        IOptions<WorkerOptions> workerOptions = null
    )
    {
        _minSyncDate = workerOptions?.Value.MinSyncDate is { } floor
            ? DateOnly.FromDateTime(floor)
            : null;
        _institutionalHoldingRepository = institutionalHoldingRepository;
        _institutionalHolderRepository = institutionalHolderRepository;
        _dailyShortVolumeRepository = dailyShortVolumeRepository;
        _shortInterestRepository = shortInterestRepository;
        _failToDeliverRepository = failToDeliverRepository;
        _documentRepository = documentRepository;
        _insiderTransactionRepository = insiderTransactionRepository;
        _form144FilingRepository = form144FilingRepository;
        _formDFilingRepository = formDFilingRepository;
        _nCenFilingRepository = nCenFilingRepository;
        _nportFilingRepository = nportFilingRepository;
        _congressionalTradeRepository = congressionalTradeRepository;
        _dailyStockPriceRepository = dailyStockPriceRepository;
        _financialFactRepository = financialFactRepository;
        _financialConceptRepository = financialConceptRepository;
        _commonStockRepository = commonStockRepository;
    }

    // Whether the holdings tab should OPEN in the combined view: the newest quarter's filing
    // window is still open and a prior quarter exists to carry non-filers forward from.
    public async Task<bool> ShouldDefaultToCombined(CommonStock stock)
    {
        var reportDates = await LoadClampedReportDates(stock);
        return reportDates.Count >= 2 && CombinedQuarterHelper.IsFilingWindowOpen(reportDates[0]);
    }

    public async Task<HoldingsTabViewModel> LoadHoldingsTab(CommonStock stock, DateOnly? date)
    {
        var reportDates = await LoadClampedReportDates(stock);

        var isCombinedAvailable =
            reportDates.Count >= 2 && CombinedQuarterHelper.IsFilingWindowOpen(reportDates[0]);

        var selectedDate = date ?? reportDates.FirstOrDefault();

        if (selectedDate == default)
        {
            return new HoldingsTabViewModel
            {
                AvailableDates = reportDates,
                SelectedDate = selectedDate,
                Ticker = stock.Ticker,
                IsCombinedAvailable = isCombinedAvailable,
            };
        }

        // Materialize the full current quarter once: drives header stats AND the
        // position-change grouping. Previously this was two round trips (sum/count
        // aggregates + a top-100 fetch); the bucketed view needs every holder anyway,
        // so the in-memory aggregates are cheaper than re-querying.
        var allCurrent = await LoadHoldingsByStockWithHolder(stock, selectedDate);

        var previousDate = reportDates.PreviousFrom(selectedDate);
        var allPrevious = previousDate.HasValue
            ? await LoadHoldingsByStockWithHolder(stock, previousDate.Value)
            : [];

        var filersWithCurrentQuarterFilings = await GetFilersWithCurrentQuarterFilings(
            allCurrent,
            allPrevious,
            selectedDate
        );

        var grouped = HoldingsPositionGrouper.Group(
            allCurrent,
            allPrevious,
            filersWithCurrentQuarterFilings
        );

        var allChanges = grouped.SelectMany(g => g.Value).ToList();
        await ApplyQuarterFirstOwned(stock, allChanges);

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
            SharesOutstanding = stock.SharesOutStanding,
            OwnershipTrend = await LoadOwnershipTrend(stock),
            GroupedHolders = grouped,
            BucketCounts = bucketCounts,
            TopBuyers = topBuyers,
            TopSellers = topSellers,
            TotalBuyerCount = HoldingsTopMoversSelector.CountBuyers(grouped),
            TotalSellerCount = HoldingsTopMoversSelector.CountSellers(grouped),
            IsCombinedAvailable = isCombinedAvailable,
        };
    }

    public async Task<HoldingsTabViewModel> LoadHoldingsCombinedTab(CommonStock stock)
    {
        var reportDates = await LoadClampedReportDates(stock);

        if (reportDates.Count < 2)
        {
            return new HoldingsTabViewModel
            {
                AvailableDates = reportDates,
                Ticker = stock.Ticker,
                IsCombinedView = true,
                IsCombinedAvailable = false,
            };
        }

        var current = reportDates[0];
        var previous = reportDates[1];

        var allCombined = await _institutionalHoldingRepository
            .GetCombinedQuarter(current, previous)
            .Where(h => h.CommonStockId == stock.Id)
            .Include(h => h.InstitutionalHolder)
            .ToListAsync();

        var holders = allCombined
            .GroupBy(h => h.InstitutionalHolderId)
            .Select(g =>
            {
                var latestDate = g.Max(h => h.ReportDate);
                return new HolderPositionChange
                {
                    InstitutionalHolderId = g.Key,
                    InstitutionalHolder = g.First().InstitutionalHolder,
                    CurrentHolding = g.OrderByDescending(h => h.FilingDate).First(),
                    CurrentShares = g.Sum(h => h.Shares),
                    CurrentValue = g.Sum(h => h.Value),
                    ChangeType = PositionChangeType.Unchanged,
                    LatestReportDate = latestDate,
                };
            })
            .ToList();

        await ApplyQuarterFirstOwned(stock, holders);

        var grouped = new Dictionary<PositionChangeType, List<HolderPositionChange>>
        {
            [PositionChangeType.Unchanged] = holders,
            [PositionChangeType.New] = [],
            [PositionChangeType.Increased] = [],
            [PositionChangeType.Reduced] = [],
            [PositionChangeType.SoldOut] = [],
        };

        return new HoldingsTabViewModel
        {
            AvailableDates = reportDates,
            SelectedDate = current,
            Ticker = stock.Ticker,
            TotalValue = allCombined.Sum(h => h.Value),
            TotalShares = allCombined.Sum(h => h.Shares),
            HolderCount = holders.Count,
            SharesOutstanding = stock.SharesOutStanding,
            OwnershipTrend = await LoadOwnershipTrend(stock),
            GroupedHolders = grouped,
            BucketCounts = grouped.ToDictionary(g => g.Key, g => g.Value.Count),
            IsCombinedView = true,
            IsCombinedAvailable = true,
        };
    }

    public async Task<ShortVolumeTabViewModel> LoadShortVolumeTab(CommonStock stock)
    {
        var shortVolumes = await FetchMostRecentAscending(
            _dailyShortVolumeRepository.GetHistoryByStock(stock),
            d => d.Date,
            90
        );
        return new ShortVolumeTabViewModel { ShortVolumes = shortVolumes, Ticker = stock.Ticker };
    }

    public async Task<ShortInterestTabViewModel> LoadShortInterestTab(CommonStock stock)
    {
        var shortInterests = await FetchMostRecentAscending(
            _shortInterestRepository.GetHistoryByStock(stock),
            s => s.SettlementDate,
            24
        );
        return new ShortInterestTabViewModel
        {
            ShortInterests = shortInterests,
            Ticker = stock.Ticker,
        };
    }

    public async Task<FtdTabViewModel> LoadFtdTab(CommonStock stock)
    {
        var ftds = await FetchMostRecentAscending(
            _failToDeliverRepository.GetByStock(stock),
            f => f.SettlementDate,
            90
        );
        return new FtdTabViewModel { FailsToDeliver = ftds, Ticker = stock.Ticker };
    }

    // Fetch the most recent N rows from a query in descending order then re-sort
    // ascending in memory so chart consumers downstream get chronological data.
    // OrderBy(orderKey.Compile()) — not List<T>.Reverse() — so ties on the
    // order key keep deterministic ordering matching the EF-side sort.
    private async Task<List<T>> FetchMostRecentAscending<T>(
        IQueryable<T> source,
        Expression<Func<T, DateOnly>> orderKey,
        int take
    )
    {
        if (_minSyncDate is { } minDate)
        {
            var atOrAfterFloor = Expression.Lambda<Func<T, bool>>(
                Expression.GreaterThanOrEqual(orderKey.Body, Expression.Constant(minDate)),
                orderKey.Parameters
            );
            source = source.Where(atOrAfterFloor);
        }
        var rows = await source.TakeMostRecent(orderKey, take).ToListAsync();
        return rows.OrderBy(orderKey.Compile()).ToList();
    }

    // Materialize the most recent RecentRowLimit rows of a query, newest first.
    // Any Include() is applied by the caller before passing the query in.
    private static Task<List<T>> TakeMostRecent<T, TKey>(
        IQueryable<T> source,
        Expression<Func<T, TKey>> orderKey
    ) => source.TakeMostRecent(orderKey, RecentRowLimit).ToListAsync();

    public async Task<DocumentsTabViewModel> LoadDocumentsTab(CommonStock stock)
    {
        var documents = await TakeMostRecent(
            _documentRepository.GetByCompany(stock),
            d => d.ReportingDate
        );
        return new DocumentsTabViewModel { Documents = documents, Ticker = stock.Ticker };
    }

    public async Task<InsiderTradingTabViewModel> LoadInsiderTradingTab(CommonStock stock)
    {
        var transactionQuery = _insiderTransactionRepository
            .GetByStockWithOwner(stock)
            .ExcludeHoldings();
        if (_minSyncDate is { } minDate)
        {
            transactionQuery = transactionQuery.Where(t => t.TransactionDate >= minDate);
        }
        var transactions = await TakeMostRecent(transactionQuery, t => t.TransactionDate);
        return new InsiderTradingTabViewModel
        {
            Transactions = transactions,
            Ticker = stock.Ticker,
        };
    }

    public async Task<ProposedSalesTabViewModel> LoadProposedSalesTab(CommonStock stock)
    {
        var filings = await TakeMostRecent(
            _form144FilingRepository.GetByStock(stock),
            f => f.FilingDate
        );
        return new ProposedSalesTabViewModel { Filings = filings, Ticker = stock.Ticker };
    }

    public async Task<ExemptOfferingsTabViewModel> LoadExemptOfferingsTab(CommonStock stock)
    {
        var filings = await TakeMostRecent(
            _formDFilingRepository.GetByStock(stock),
            f => f.FilingDate
        );
        return new ExemptOfferingsTabViewModel { Filings = filings, Ticker = stock.Ticker };
    }

    // Whether the stock has any fund-only filings, used to decide if the
    // Fund Operations (N-CEN) and Fund Holdings (NPORT) tabs are shown at all.
    // Operating companies file neither, so both flags are false for them.
    public async Task<(bool HasFundHoldings, bool HasFundOperations)> LoadFundTabAvailability(
        CommonStock stock
    )
    {
        var hasFundHoldings = await _nportFilingRepository.GetByStock(stock).AnyAsync();
        var hasFundOperations = await _nCenFilingRepository.GetByStock(stock).AnyAsync();
        return (hasFundHoldings, hasFundOperations);
    }

    public async Task<FundOperationsTabViewModel> LoadFundOperationsTab(CommonStock stock)
    {
        var filings = await TakeMostRecent(
            _nCenFilingRepository.GetByStock(stock).Include(f => f.ServiceProviders),
            f => f.FilingDate
        );
        return new FundOperationsTabViewModel { Filings = filings, Ticker = stock.Ticker };
    }

    public async Task<FundHoldingsTabViewModel> LoadFundHoldingsTab(CommonStock stock)
    {
        var filing = await _nportFilingRepository
            .GetByStock(stock)
            .OrderByDescending(f => f.FilingDate)
            .FirstOrDefaultAsync();

        if (filing == null)
            return new FundHoldingsTabViewModel { Ticker = stock.Ticker };

        // NPORT-P reports can carry thousands of positions; show the largest by value and
        // report the full count rather than loading the whole schedule into the page.
        var holdings = await TakeMostRecent(
            _nportFilingRepository.GetHoldings(filing),
            h => h.ValueUsd
        );

        var totalHoldings = await _nportFilingRepository.GetHoldings(filing).CountAsync();

        return new FundHoldingsTabViewModel
        {
            Ticker = stock.Ticker,
            Filing = filing,
            Holdings = holdings,
            TotalHoldings = totalHoldings,
        };
    }

    public async Task<CongressionalTradesTabViewModel> LoadCongressionalTradesTab(CommonStock stock)
    {
        IQueryable<Congress.Data.Models.CongressionalTrade> tradeQuery =
            _congressionalTradeRepository.GetByStock(stock).Include(t => t.CongressMember);
        if (_minSyncDate is { } minDate)
        {
            tradeQuery = tradeQuery.Where(t => t.TransactionDate >= minDate);
        }
        var trades = await TakeMostRecent(tradeQuery, t => t.TransactionDate);
        return new CongressionalTradesTabViewModel { Trades = trades, Ticker = stock.Ticker };
    }

    public async Task<PriceTabViewModel> LoadPriceTab(CommonStock stock)
    {
        var priceQuery = _dailyStockPriceRepository.GetByStock(stock);
        if (_minSyncDate is { } minDate)
        {
            // Clamp the indicators too: derived series (SMA, RSI, MACD) over
            // partial pre-floor data would be just as misleading as the chart.
            priceQuery = priceQuery.Where(p => p.Date >= minDate);
        }
        var prices = await priceQuery.OrderBy(p => p.Date).ToListAsync();

        var closePrices = prices.Select(p => p.Close).ToList();
        var (macdLine, macdSignal, macdHistogram) = TechnicalIndicatorService.ComputeMacd(
            closePrices
        );

        var returns = PriceReturnCalculator.Compute(
            prices.Select(p => p.Date).ToList(),
            closePrices
        );

        var sma50 = TechnicalIndicatorService.ComputeSma(closePrices, 50);
        var sma200 = TechnicalIndicatorService.ComputeSma(closePrices, 200);
        var (streakDays, streakDirection) = TechnicalIndicatorService.CountConsecutiveStreak(
            closePrices
        );

        // Middle band is the 20-bar SMA (already exposed as Sma20), so discard it here.
        var (_, bollingerUpper, bollingerLower) = TechnicalIndicatorService.ComputeBollingerBands(
            closePrices
        );

        return new PriceTabViewModel
        {
            Ticker = stock.Ticker,
            Prices = prices,
            Returns = returns,
            BenchmarkTicker = BenchmarkTicker,
            BenchmarkReturns = await LoadBenchmarkReturns(stock, prices),
            Sma20 = TechnicalIndicatorService.ComputeSma(closePrices, 20),
            Sma50 = sma50,
            Sma200 = sma200,
            Rsi14 = TechnicalIndicatorService.ComputeRsi(closePrices),
            MacdLine = macdLine,
            MacdSignal = macdSignal,
            MacdHistogram = macdHistogram,
            BollingerUpper = bollingerUpper,
            BollingerLower = bollingerLower,
            MaCross = TechnicalIndicatorService.DetectMaCross(sma50, sma200),
            PriceStreakDays = streakDays,
            PriceStreakDirection = streakDirection,
        };
    }

    // Returns for the benchmark (SPY) over the same windows, so the Price tab can
    // show out/under-performance. Null when there's no benchmark to compare against:
    // the stock has no prices, the benchmark isn't tracked, this stock IS the
    // benchmark, or the benchmark has no prices in the lookback window.
    private async Task<PriceReturns> LoadBenchmarkReturns(
        CommonStock stock,
        List<DailyStockPrice> stockPrices
    )
    {
        if (stockPrices.Count == 0)
            return null;

        var benchmark = await _commonStockRepository.GetByPrimaryTicker(BenchmarkTicker);
        if (benchmark == null || benchmark.Id == stock.Id)
            return null;

        var latestDate = stockPrices[^1].Date;
        var benchmarkPrices = await _dailyStockPriceRepository
            .GetByStock(benchmark, latestDate.AddDays(-BenchmarkLookbackDays), latestDate)
            .OrderBy(p => p.Date)
            .ToListAsync();

        if (benchmarkPrices.Count == 0)
            return null;

        return PriceReturnCalculator.Compute(
            benchmarkPrices.Select(p => p.Date).ToList(),
            benchmarkPrices.Select(p => p.Close).ToList()
        );
    }

    public async Task<FinancialsTabViewModel> LoadFinancialsTab(
        CommonStock stock,
        FinancialStatementType statementType,
        int? year,
        SecFiscalPeriod? period
    )
    {
        var availablePeriods = await BuildAvailablePeriods(stock);

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

        viewModel.Lines = await BuildStatementLines(
            stock,
            statementType,
            selected.FiscalYear,
            selected.FiscalPeriod
        );

        return viewModel;
    }

    private async Task<List<FinancialsPeriodOption>> BuildAvailablePeriods(CommonStock stock)
    {
        // Only periods with an actual STATEMENT fact qualify: filer-extension
        // (Custom) KPI facts also live in the consolidated context now, and a
        // KPI-only (year, period) would otherwise offer a dropdown option whose
        // statement table renders empty.
        var statementLines = Enum.GetValues<FinancialStatementType>()
            .SelectMany(FinancialStatementConcepts.For)
            .ToList();
        var statementTaxonomies = statementLines.Select(l => l.Taxonomy).Distinct().ToList();
        var statementTags = statementLines.Select(l => l.Tag).Distinct().ToList();
        var statementConceptIds = await _financialConceptRepository
            .GetMatching(statementTaxonomies, statementTags)
            .Select(c => c.Id)
            .ToListAsync();

        // Distinct (year, period) pairs the company actually reported. This is a
        // separate round trip from the per-period fact query below — both are
        // covered by the [CommonStockId, FiscalYear, FiscalPeriod] index, and
        // keeping them separate avoids loading every fact just to list periods.
        var periodKeys = await _financialFactRepository
            .GetConsolidatedByStock(stock)
            .Where(f => statementConceptIds.Contains(f.FinancialConceptId))
            .Select(f => new { f.FiscalYear, f.FiscalPeriod })
            .Distinct()
            .ToListAsync();

        // SecFiscalPeriod's enum ordinal (FullYear=0, Q1=1…Q4=4) is not
        // chronological — ordering by it would float the annual figure to the
        // wrong end. The 10-K (FullYear) is filed after Q4 and is the canonical
        // annual number, so rank it last within its year; default selection
        // (first option) is then the latest year's annual statement.
        return periodKeys
            .OrderByDescending(p => p.FiscalYear)
            .ThenByDescending(p => ChronologicalRank(p.FiscalPeriod))
            .Select(p => new FinancialsPeriodOption(
                p.FiscalYear,
                p.FiscalPeriod,
                $"FY{p.FiscalYear} {p.FiscalPeriod.NameForHumans()}"
            ))
            .ToList();
    }

    private async Task<List<FinancialsLineViewModel>> BuildStatementLines(
        CommonStock stock,
        FinancialStatementType statementType,
        int fiscalYear,
        SecFiscalPeriod fiscalPeriod
    )
    {
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
            .GetConsolidatedByStock(stock)
            .Where(f =>
                f.FiscalYear == fiscalYear
                && f.FiscalPeriod == fiscalPeriod
                && conceptIds.Contains(f.FinancialConceptId)
            )
            .ToListAsync();

        // SEC re-emits a concept across filings (restatements); the latest-filed
        // value is the currently-reported one.
        var latestByConcept = facts
            .LatestPerGroup(f => f.FinancialConceptId, f => f.FiledDate)
            .ToDictionary(f => f.FinancialConceptId);

        return statementLines
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

    public async Task<KeyMetricsViewModel> LoadKeyMetrics(CommonStock stock)
    {
        var vm = new KeyMetricsViewModel { MarketCapitalization = stock.MarketCapitalization };

        var recentPrices = await _dailyStockPriceRepository
            .GetByStock(stock)
            .OrderByDescending(p => p.Date)
            .Take(252)
            .ToListAsync();

        if (recentPrices.Count > 0)
        {
            vm.LatestClose = recentPrices[0].Close;
            vm.High52Week = recentPrices.Max(p => p.High);
            vm.Low52Week = recentPrices.Min(p => p.Low);
        }

        var epsConcept = await _financialConceptRepository
            .GetMatching([FactTaxonomy.UsGaap], ["EarningsPerShareDiluted"])
            .Select(c => c.Id)
            .FirstOrDefaultAsync();

        if (epsConcept != Guid.Empty)
        {
            var epsFact = await _financialFactRepository
                .GetConsolidatedByStock(stock)
                .Where(f =>
                    f.FinancialConceptId == epsConcept && f.FiscalPeriod == SecFiscalPeriod.FullYear
                )
                .OrderByDescending(f => f.FiscalYear)
                .ThenByDescending(f => f.FiledDate)
                .FirstOrDefaultAsync();

            if (epsFact != null)
            {
                vm.EpsDiluted = epsFact.Value;
                if (vm.LatestClose.HasValue && epsFact.Value != 0)
                    vm.PeRatio = Math.Round(vm.LatestClose.Value / epsFact.Value, 2);
            }
        }

        return vm;
    }

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

    // Stamp each holder's first-owned quarter for this stock via one batched lookup.
    private async Task ApplyQuarterFirstOwned(CommonStock stock, List<HolderPositionChange> changes)
    {
        var holderIds = changes.Select(h => h.InstitutionalHolderId).Distinct().ToList();
        var firstOwned = await _institutionalHoldingRepository
            .GetFirstOwnedQuarters(stock, holderIds)
            .ToDictionaryAsync(kv => kv.Key, kv => kv.Value);
        foreach (var change in changes)
        {
            if (firstOwned.TryGetValue(change.InstitutionalHolderId, out var quarter))
                change.QuarterFirstOwned = quarter;
        }
    }

    private Task<List<InstitutionalHolding>> LoadHoldingsByStockWithHolder(
        CommonStock stock,
        DateOnly reportDate
    ) => _institutionalHoldingRepository.Get13FByStockWithHolder(stock, reportDate).ToListAsync();

    // Report dates for the holdings tab, newest first, clamped to the sync
    // floor: quarters before it hold partial filings, so their dates must not
    // appear in the selector, the stats, or the combined view's quarter pair.
    private Task<List<DateOnly>> LoadClampedReportDates(CommonStock stock)
    {
        var datesQuery = _institutionalHoldingRepository.Get13FReportDatesByStock(stock);
        if (_minSyncDate is { } minDate)
        {
            datesQuery = datesQuery.Where(d => d >= minDate);
        }
        return datesQuery.ToListAsync();
    }

    // Mirrors the header-stat semantics per report date: Shares summed over every
    // row (share classes included), holders counted distinct — so the trend's
    // latest point matches the stats shown for the latest quarter.
    private async Task<List<OwnershipTrendPoint>> LoadOwnershipTrend(CommonStock stock)
    {
        var holdingsQuery = _institutionalHoldingRepository.Get13FHistoryByStock(stock);
        if (_minSyncDate is { } trendFloor)
        {
            holdingsQuery = holdingsQuery.Where(h => h.ReportDate >= trendFloor);
        }
        var points = await holdingsQuery
            .GroupBy(h => h.ReportDate)
            .Select(g => new
            {
                ReportDate = g.Key,
                TotalShares = g.Sum(h => h.Shares),
                HolderCount = g.Select(h => h.InstitutionalHolderId).Distinct().Count(),
            })
            .OrderBy(p => p.ReportDate)
            .ToListAsync();

        return points
            .Select(p => new OwnershipTrendPoint
            {
                ReportDate = p.ReportDate,
                TotalShares = p.TotalShares,
                HolderCount = p.HolderCount,
            })
            .ToList();
    }

    // Narrow the Sold-Out filter to only holders who were in the previous
    // quarter but are absent from the current quarter for this stock. The
    // old query fetched ALL distinct filers market-wide for the quarter —
    // millions of rows — just to build a HashSet. We only need to know
    // whether each "gap" holder filed any 13F this quarter at all.
    private async Task<HashSet<Guid>> GetFilersWithCurrentQuarterFilings(
        List<InstitutionalHolding> allCurrent,
        List<InstitutionalHolding> allPrevious,
        DateOnly selectedDate
    )
    {
        var currentHolderIds = allCurrent.Select(h => h.InstitutionalHolderId).ToHashSet();
        var previousOnlyHolderIds = allPrevious
            .Select(h => h.InstitutionalHolderId)
            .Where(id => !currentHolderIds.Contains(id))
            .Distinct()
            .ToList();

        if (previousOnlyHolderIds.Count == 0)
            return [];

        return (
            await _institutionalHoldingRepository
                .GetAll()
                .Where(h =>
                    h.ReportDate == selectedDate
                    && previousOnlyHolderIds.Contains(h.InstitutionalHolderId)
                )
                .Select(h => h.InstitutionalHolderId)
                .Distinct()
                .ToListAsync()
        ).ToHashSet();
    }
}
