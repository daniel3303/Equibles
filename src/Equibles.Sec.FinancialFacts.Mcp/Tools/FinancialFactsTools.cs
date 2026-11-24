using System.ComponentModel;
using System.Globalization;
using System.Text;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Core.Extensions;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.ReleaseStatements;
using Equibles.Sec.FinancialFacts.BusinessLogic.Splits;
using Equibles.Sec.FinancialFacts.Data;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.Mcp.Captures;
using Equibles.Sec.FinancialFacts.Mcp.Helpers;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Sec.FinancialFacts.Mcp.Tools;

[McpServerToolType]
public class FinancialFactsTools
{
    private const int MaxResultsCap = 200;
    private const int MaxTickers = 25;
    private const int MaxUnmarkedSeriesLagDays = 550;

    private readonly FinancialFactRepository _financialFactRepository;
    private readonly FinancialConceptRepository _financialConceptRepository;
    private readonly EquityIssuerRepository _commonStockRepository;
    private readonly StockSplitRepository _stockSplitRepository;
    private readonly DocumentRepository _documentRepository;
    private readonly ReleaseStatementFigureRepository _releaseFigureRepository;
    private readonly McpToolRunner _runner;

    // The document repository is optional: a host without the filings module has no stored
    // earnings release to point at.
    public FinancialFactsTools(
        FinancialFactRepository financialFactRepository,
        FinancialConceptRepository financialConceptRepository,
        EquityIssuerRepository commonStockRepository,
        StockSplitRepository stockSplitRepository,
        ErrorManager errorManager,
        ILogger<FinancialFactsTools> logger,
        DocumentRepository documentRepository = null,
        ReleaseStatementFigureRepository releaseFigureRepository = null
    )
    {
        _documentRepository = documentRepository;
        _releaseFigureRepository = releaseFigureRepository;
        _financialFactRepository = financialFactRepository;
        _financialConceptRepository = financialConceptRepository;
        _commonStockRepository = commonStockRepository;
        _stockSplitRepository = stockSplitRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetFinancialFact", Title = "Financial Concept Lookup", ReadOnly = true)]
    [Description(
        "Get a single financial concept (e.g. revenue, net income, diluted EPS, total "
            + "assets, operating cash flow) over time for a company, sourced from SEC "
            + "Company Facts (structured XBRL), or from the issuer's regulatory filings for a "
            + "listing outside the US. Returns a time series using the latest "
            + "restated value unless asOriginallyReported is set: one row per fiscal period for "
            + "flows, one row per as-of date for balances and share counts. "
            + "Each row carries its actual period start/end; fiscal years/quarters follow the "
            + "company's own fiscal calendar. Warns when the selected alias ends materially "
            + "before the company's other structured facts, which can indicate an XBRL tag "
            + "change. Adds a newer earnings release's figures as preliminary, or names that "
            + "8-K. "
            + "For income and cash-flow lines whose quarters add up to the year, a quarter the filer "
            + "reports only inside a cumulative figure (six or nine months, or the year) is worked out "
            + "from that year's own figures and marked (derived). "
            + "Dimensioned disclosures such as customer concentration are outside this "
            + "consolidated-series tool. For a full statement use GetFinancialStatement; to "
            + "compare peers use CompareFinancialFact."
    )]
    public Task<string> GetFinancialFact(
        [Description(
            "Stock ticker symbol (e.g., AAPL, MSFT); a listing on another exchange is MIC:TICKER (XPAR:AIR)."
        )]
            string ticker,
        [Description(
            "Concept alias, e.g. 'revenue', 'net-income', 'eps-diluted', 'total-assets', "
                + "'operating-cash-flow'. Call with an unknown value to list supported aliases."
        )]
            string concept,
        [Description("Optional SEC form filter, e.g. '10-K' or '10-Q'")] string form = null,
        [Description("Optional earliest period-end date, YYYY-MM-DD")] string fromDate = null,
        [Description("Optional latest period-end date, YYYY-MM-DD")] string toDate = null,
        [Description("Maximum periods to return, newest first (default 40, max 200)")]
            int maxResults = 40,
        [Description(
            "When true, show the earliest canonical periodic filing instead of the latest "
                + "restatement within that source priority. Default false."
        )]
            bool asOriginallyReported = false,
        [Description(
            "Optional fiscal-period filter: 'FY' (annual only) or 'Q1'..'Q4'. A fourth quarter "
                + "the filer never reported on its own is the year less its first three quarters, "
                + "marked (derived), for income and cash-flow lines; other lines have Q4 rows only "
                + "where the filer reported one."
        )]
            string fiscalPeriod = null
    )
    {
        return _runner.Execute(
            async () =>
            {
                if (string.IsNullOrWhiteSpace(ticker))
                    return "A ticker symbol is required.";

                if (string.IsNullOrWhiteSpace(concept))
                    return $"A concept is required. {SupportedAliasesNote()}";

                var (stock, stockError) =
                    await _commonStockRepository.ResolveIssuerIncludingDelisted(ticker);
                if (stockError != null)
                    return stockError;

                if (!FinancialConceptAliases.TryResolve(concept, out var conceptRefs))
                    return $"Unknown concept '{concept}'. {SupportedAliasesNote()}";

                DocumentType formFilter = null;
                if (!string.IsNullOrWhiteSpace(form))
                {
                    formFilter = DocumentType.FromDisplayName(form.Trim());
                    if (formFilter == null)
                        return $"Unknown form '{form}'. Use e.g. '10-K' or '10-Q'.";
                }

                if (!TryParseBound(fromDate, out var fromBound))
                    return $"Unknown date '{fromDate}'. Use YYYY-MM-DD.";
                if (!TryParseBound(toDate, out var toBound))
                    return $"Unknown date '{toDate}'. Use YYYY-MM-DD.";

                SecFiscalPeriod? periodFilter = null;
                if (!string.IsNullOrWhiteSpace(fiscalPeriod))
                {
                    if (!FactArgs.TryParsePeriod(fiscalPeriod, out var parsedPeriod))
                        return McpOutput.InvalidArgument(
                            "fiscalPeriod",
                            fiscalPeriod,
                            "'FY' or 'Q1'..'Q4'"
                        );
                    periodFilter = parsedPeriod;
                }

                // conceptId → alias priority (0 = the alias's primary tag). Used
                // to break ties deterministically when one period carries facts
                // under several of the alias's tags (the ASC 606 transition).
                var (conceptPriority, conceptsById) = await ResolveConcepts(conceptRefs);
                if (conceptPriority.Count == 0)
                    return $"No '{concept}' data has been ingested for {EquityListingSymbol.Display(stock)}.";

                var issuerFacts = _financialFactRepository.GetConsolidatedByIssuerId(stock.Id);
                List<FinancialFact> facts;
                // Depreciation and amortization leaves out a slice tagged beside the total and
                // sums its two parts where no total is tagged (DepreciationTotal).
                if (DepreciationTotal.IsFamily(conceptRefs))
                {
                    var depreciation = await DepreciationTotalFacts.Load(
                        _financialFactRepository,
                        _financialConceptRepository,
                        [stock.Id]
                    );
                    (facts, conceptPriority) = (depreciation.Facts, depreciation.Priority);
                }
                else
                    facts = await issuerFacts
                        .Where(f => conceptPriority.Keys.Contains(f.FinancialConceptId))
                        .ToListAsync();
                if (BankTopLine.IsRevenue(conceptRefs))
                {
                    var bankComponents = await LoadBankTopLineComponents(issuerFacts);
                    (facts, conceptPriority) = BankTopLine.Fold(
                        facts,
                        bankComponents.NetInterestIncome,
                        bankComponents.NoninterestIncome,
                        conceptPriority,
                        bankComponents.InterestExpense
                    );
                }
                // The operating-expenses and total-costs lines continue through a filer's
                // operating costs where its history proves them one line (OperatingCostsContinuation).
                var claimableAlias = OperatingCostsContinuation.ClaimableAlias(conceptRefs);
                if (claimableAlias != null)
                {
                    var operatingCosts = await LoadOperatingCostsClaimedBy(
                        claimableAlias,
                        issuerFacts
                    );
                    (facts, conceptPriority) = OperatingCostsContinuation.Fold(
                        claimableAlias,
                        claimableAlias,
                        facts,
                        conceptPriority,
                        operatingCosts.Facts
                    );
                    foreach (var (conceptId, conceptRef) in operatingCosts.ConceptsById)
                        conceptsById.TryAdd(conceptId, conceptRef);
                }
                var companyLatestPeriodEnd = await _financialFactRepository
                    .GetConsolidatedByIssuerId(stock.Id)
                    .MaxAsync(f => (DateOnly?)f.PeriodEnd);
                var aliasLatestPeriodEnd =
                    facts.Count == 0 ? (DateOnly?)null : facts.Max(f => f.PeriodEnd);
                var coverageNote = BuildCoverageNote(
                    EquityListingSymbol.Display(stock),
                    aliasLatestPeriodEnd,
                    companyLatestPeriodEnd
                );

                // A quarter the filer reports only inside a cumulative figure (six or nine months, or
                // the year) is worked out from that fiscal year's own figures and marked; a form filter
                // or the as-originally-reported reading asks for filed figures only.
                var derived = new HashSet<FinancialFact>(ReferenceEqualityComparer.Instance);
                List<FinancialFact> picked;
                if (FinancialConceptAliases.IsInstant(concept))
                    picked = PickInstantRows(
                        facts,
                        conceptPriority,
                        asOriginallyReported,
                        formFilter
                    );
                else if (
                    formFilter == null
                    && !asOriginallyReported
                    && FinancialConceptAliases.IsAdditiveFlow(concept)
                )
                    picked = PickFlowRowsWithDerivedQuarters(
                        facts,
                        conceptPriority,
                        StatementQuarterDerivation.IsNonNegative(
                            FinancialConceptAliases.Canonical(concept)
                        ),
                        derived
                    );
                else
                    picked = PickFlowRows(facts, conceptPriority, asOriginallyReported, formFilter);
                var perPeriod = picked
                    .Where(f =>
                        (fromBound == null || f.PeriodEnd >= fromBound.Value)
                        && (toBound == null || f.PeriodEnd <= toBound.Value)
                        && (periodFilter == null || f.FiscalPeriod == periodFilter.Value)
                    )
                    .OrderByDescending(f => f.PeriodEnd)
                    .ToList();

                var shown = perPeriod.Take(Math.Clamp(maxResults, 1, MaxResultsCap)).ToList();

                // A request bounded to history is not asking for the period the release covers.
                var newerRelease = await NewerEarningsRelease.Find(
                    _financialFactRepository,
                    _documentRepository,
                    stock.Id
                );
                // The release's own figures for this line where the lane placed them, else the
                // pointer to the release. A form filter asks for one periodic form's facts.
                var preliminary =
                    formFilter == null
                        ? PreliminaryReleaseFigures.Render(
                            ReleaseFigureReader
                                .ForAlias(
                                    await PreliminaryReleaseFigures.Load(
                                        _releaseFigureRepository,
                                        _financialFactRepository,
                                        _stockSplitRepository,
                                        stock.Id
                                    ),
                                    concept
                                )
                                .Where(figure =>
                                    (fromBound == null || figure.PeriodEnd >= fromBound.Value)
                                    && (toBound == null || figure.PeriodEnd <= toBound.Value)
                                    && (
                                        periodFilter == null
                                        || ReleaseFigureReader
                                            .PeriodOf(figure)
                                            .Answers(periodFilter.Value)
                                    )
                                )
                                .Select(figure => ((string)null, figure))
                                .ToList(),
                            stock
                        )
                        : "";
                var releaseNote =
                    preliminary.Length > 0 ? preliminary
                    : newerRelease != null
                    && (toBound == null || toBound.Value > newerRelease.LatestPeriodEnd)
                        ? newerRelease.Note
                    : "";

                if (shown.Count == 0)
                    return $"No '{concept}' data found for {EquityListingSymbol.Display(stock)} with the given filters."
                        + releaseNote;

                var splits = shown.Any(f => SplitKind(f, conceptsById) != FactSplitKind.None)
                    ? await _stockSplitRepository
                        .GetEffectiveByStock(stock.Id, DateOnly.FromDateTime(DateTime.UtcNow))
                        .ToListAsync()
                    : [];

                var (table, capture) = RenderFactHistory(
                    concept,
                    stock,
                    asOriginallyReported,
                    shown,
                    perPeriod.Count,
                    coverageNote,
                    splits,
                    conceptsById,
                    derived
                );
                var notes = capture.Notes.ToList();
                if (shown.Any(DepreciationTotal.IsSum))
                {
                    table += SumFootnote;
                    notes.Add(SumFootnoteCardText);
                }
                if (shown.Any(derived.Contains))
                {
                    table += DerivedFootnote;
                    notes.Add(DerivedFootnoteCardText);
                }
                // The rows keep the filer's tagged figure; a gross profit that is the period's
                // operating income is named, and the statement shows the derived figure.
                var tagWarning = GrossProfitMistag.IsGrossProfit(conceptRefs)
                    ? GrossProfitMistag.Warning(
                        await MistaggedGrossProfitRows(issuerFacts, shown, asOriginallyReported),
                        stock.FiscalYearLabelOffset
                    )
                    : null;
                table += releaseNote;
                // A card keeps two notes, so the line that changes the reading of every figure comes first.
                if (preliminary.Length > 0)
                {
                    notes.Insert(0, PreliminaryReleaseFigures.CardLead);
                }
                else if (newerRelease != null && releaseNote.Length > 0)
                {
                    notes.Insert(0, newerRelease.CardNote);
                }
                // The card's notes leave out the answer's pointers to other tools, which only a model can follow.
                if (tagWarning != null)
                {
                    var warning =
                        $"{tagWarning} GetFinancialStatement shows Revenue − Cost of Revenue for those periods.";
                    table += $"\n_{warning}_\n";
                    notes.Add(tagWarning);
                }
                McpResultCapture.Record(capture with { Notes = notes });
                return table;
            },
            "GetFinancialFact",
            $"ticker: {FactMarkdown.Clean(ticker)}, concept: {FactMarkdown.Clean(concept)}, "
                + $"form: {FactMarkdown.Clean(form)}, "
                + $"fiscalPeriod: {FactMarkdown.Clean(fiscalPeriod)}"
        );
    }

    // A flow is one row per fiscal period. Reported discrete fourth quarters hide under fp=FY, so
    // they are promoted first; the alias's full history is the transition-stub corroboration context.
    // The picks are deduped by reporting span, since a bucket can hold only a comparative re-report.
    private static List<FinancialFact> PickFlowRows(
        List<FinancialFact> facts,
        IReadOnlyDictionary<Guid, int> conceptPriority,
        bool asOriginallyReported,
        DocumentType formFilter
    ) =>
        FinancialFactSeriesShaper.DedupeByReportingSpan(
            ReportedQuarterPromotion
                .WithPromotedFourthQuarters(facts)
                .Where(f => formFilter == null || f.Form == formFilter)
                .GroupBy(f => (f.FiscalYear, f.FiscalPeriod))
                .Select(g =>
                    FinancialFactSeriesShaper.PickBestFact(
                        g,
                        conceptPriority,
                        asOriginallyReported,
                        facts
                    )
                )
                .Where(f => f != null)
        );

    // The flow rows plus the quarters they leave out: a second or third quarter as the cumulative
    // figure less the one before it, then a fourth as the shown year less its shown three quarters.
    // Each added row lands in derived.
    private static List<FinancialFact> PickFlowRowsWithDerivedQuarters(
        List<FinancialFact> facts,
        IReadOnlyDictionary<Guid, int> conceptPriority,
        bool nonNegative,
        HashSet<FinancialFact> derived
    )
    {
        var filed = new HashSet<FinancialFact>(facts, ReferenceEqualityComparer.Instance);
        var withQuarters = StatementQuarterDerivation
            .AppendDerived(facts, nonNegative ? conceptPriority.Keys.ToHashSet() : null)
            .ToList();
        derived.UnionWith(withQuarters.Where(f => !filed.Contains(f)));
        var picked = PickFlowRows(withQuarters, conceptPriority, false, null);
        var shown = new HashSet<FinancialFact>(picked, ReferenceEqualityComparer.Instance);
        var withFourth = DerivedFourthQuarter.Append(picked, nonNegative);
        derived.UnionWith(withFourth.Where(f => !shown.Contains(f)));
        return withFourth;
    }

    // A balance or share count is one row per as-of date, so a cover-page count and the quarter-end
    // balance-sheet count both publish. The date and fiscal-period filters then apply to the rows.
    private static List<FinancialFact> PickInstantRows(
        List<FinancialFact> facts,
        IReadOnlyDictionary<Guid, int> conceptPriority,
        bool asOriginallyReported,
        DocumentType formFilter
    ) =>
        FinancialFactSeriesShaper.PickPerDate(
            facts.Where(f => formFilter == null || f.Form == formFilter),
            conceptPriority,
            asOriginallyReported
        );

    [McpServerTool(
        Name = "CompareFinancialFact",
        Title = "Compare Financials Across Companies",
        ReadOnly = true
    )]
    [Description(
        "Compare one financial concept across several companies for the same fiscal "
            + "period — peer comparison. Returns one row per ticker with the "
            + "latest-restated value; tickers with no data for the period are listed "
            + "separately. By default fiscal year/period follow each company's OWN fiscal calendar "
            + "(e.g. NVDA's fiscal 2025 ended January 2025), so peer rows can cover very "
            + "different calendar months — check the Period End column. For a calendar year "
            + "('2025 revenue of NVDA, AMD and INTC'), pass basis 'calendar': each company's "
            + "fiscal year holding most of its days in that year."
    )]
    public Task<string> CompareFinancialFact(
        [Description(
            "Ticker symbols to compare (max 25); a listing on another exchange is MIC:TICKER (XPAR:AIR)."
        )]
            string[] tickers,
        [Description(
            "Concept alias, e.g. 'revenue', 'net-income', 'eps-diluted'. Call with an "
                + "unknown value to list supported aliases."
        )]
            string concept,
        [Description(
            "The year compared, e.g. 2025. With basis 'fiscal': each company's period ending in that "
                + "calendar year, so a January year end covers mostly the year before (NVDA's fiscal "
                + "2025 ended January 2025); a company that names that year for the year it starts in "
                + "(most January year-end retailers) is labelled with its own name. With basis "
                + "'calendar': each company's fiscal year holding most of its days in that year."
        )]
            int fiscalYear,
        [Description("Fiscal period: 'FY' (default) or 'Q1'..'Q4'")] string fiscalPeriod = "FY",
        [Description(
            "'fiscal' (default): periods by each company's fiscal calendar. 'calendar': fiscal years "
                + "lined up by the calendar year holding most of their days; FY only."
        )]
            string basis = null
    )
    {
        return _runner.Execute(
            async () =>
            {
                if (string.IsNullOrWhiteSpace(concept))
                    return $"A concept is required. {SupportedAliasesNote()}";

                if (!FinancialConceptAliases.TryResolve(concept, out var conceptRefs))
                    return $"Unknown concept '{concept}'. {SupportedAliasesNote()}";

                if (!FactArgs.TryParsePeriod(fiscalPeriod, out var period))
                    return $"Unknown period '{fiscalPeriod}'. Use 'FY' or 'Q1'..'Q4'.";

                var basisName = string.IsNullOrWhiteSpace(basis)
                    ? "fiscal"
                    : basis.Trim().ToLowerInvariant();
                if (basisName is not ("fiscal" or "calendar"))
                    return McpOutput.InvalidArgument("basis", basis, "'fiscal' or 'calendar'");
                var calendar = basisName == "calendar";
                // Calendar quarters would place discrete quarters, which many filers never report for Q4.
                if (calendar && period != SecFiscalPeriod.FullYear)
                    return "basis 'calendar' lines up whole fiscal years only; use fiscalPeriod 'FY'.";

                var (requested, tickerError) = ParseComparisonTickers(tickers);
                if (tickerError != null)
                    return tickerError;

                var (conceptPriority, conceptsById) = await ResolveConcepts(conceptRefs);
                if (conceptPriority.Count == 0)
                    return $"No '{concept}' data has been ingested.";

                // Two queries instead of 2N: batch-load the requested stocks,
                // then all matching facts in one go keyed by company.
                var stocks = await _commonStockRepository.GetUsByTickers(requested).ToListAsync();
                var stockByTicker = BuildComparisonStockMap(requested, stocks);
                // A venue symbol, or a provider suffix the US directory does not know, names a
                // listing outside the US; its issuer's facts compare like any other.
                foreach (
                    var ticker in requested.Where(ticker =>
                        !stockByTicker.ContainsKey(ticker)
                        && (
                            ticker.Contains(EquityListingSymbol.DisplaySeparator)
                            || ticker.Contains('.')
                        )
                    )
                )
                {
                    var (venueStock, venue, _) = await _commonStockRepository.ResolveListing(
                        ticker
                    );
                    if (venue == null)
                        continue;
                    stockByTicker.Add(ticker, venueStock);
                    if (stocks.All(stock => stock.Id != venueStock.Id))
                        stocks.Add(venueStock);
                }

                var stockIds = stocks.Select(s => s.Id).ToList();
                // The revenue alias also loads a bank's net interest and noninterest income,
                // which stand in for a missing or partial revenue line (BankTopLine).
                var bankComponentIds = BankTopLine.IsRevenue(conceptRefs)
                    ? await ResolveBankTopLineComponentIds()
                    : (
                        NetInterestIncome: new HashSet<Guid>(),
                        NoninterestIncome: new HashSet<Guid>(),
                        InterestExpense: new HashSet<Guid>()
                    );
                // The cost lines continue through operating costs per issuer
                // (OperatingCostsContinuation); gross profit loads what proves a mis-tag.
                var claimableAlias = OperatingCostsContinuation.ClaimableAlias(conceptRefs);
                var operatingCosts =
                    claimableAlias == null
                        ? (Ids: new HashSet<Guid>(), ClaimedStockIds: new HashSet<Guid>())
                        : await ResolveOperatingCostsClaims(claimableAlias, stockIds, conceptsById);
                var grossProfitComponents = GrossProfitMistag.IsGrossProfit(conceptRefs)
                    ? await ResolveGrossProfitComponents()
                    : [];
                // One fiscal year's pick per company, and the companies whose gross profit that pick proves
                // mis-tagged. A calendar comparison runs it for the two years that can hold the calendar year.
                async Task<(Dictionary<Guid, FinancialFact> Best, HashSet<Guid> Mistagged)> Slice(
                    int year
                )
                {
                    var loadedConceptIds = conceptPriority
                        .Keys.Concat(bankComponentIds.NetInterestIncome)
                        .Concat(bankComponentIds.NoninterestIncome)
                        .Concat(bankComponentIds.InterestExpense)
                        .Concat(operatingCosts.Ids)
                        .Concat(grossProfitComponents.SelectMany(c => c.Ids))
                        .ToList();
                    // A Q4 request must also load the year's fp=FY rows: filers whose
                    // Company Facts carry Q4 frames report the discrete fourth quarter
                    // under the FullYear stamp (see ReportedQuarterPromotion).
                    var includeFullYearForQ4 = period == SecFiscalPeriod.Q4;
                    var facts = await _financialFactRepository
                        .GetConsolidatedByIssuerIds(stockIds)
                        .Where(f =>
                            f.FiscalYear == year
                            && (
                                f.FiscalPeriod == period
                                || (
                                    includeFullYearForQ4
                                    && f.FiscalPeriod == SecFiscalPeriod.FullYear
                                )
                            )
                            && loadedConceptIds.Contains(f.FinancialConceptId)
                        )
                        .ToListAsync();
                    // Depreciation and amortization is judged on each issuer's whole history, so the
                    // period compared shows the figure GetFinancialFact shows (DepreciationTotal).
                    if (DepreciationTotal.IsFamily(conceptRefs))
                    {
                        var depreciation = await DepreciationTotalFacts.Load(
                            _financialFactRepository,
                            _financialConceptRepository,
                            stockIds
                        );
                        conceptPriority = depreciation.Priority;
                        facts = depreciation
                            .Facts.Where(f =>
                                f.FiscalYear == year
                                && (
                                    f.FiscalPeriod == period
                                    || (
                                        includeFullYearForQ4
                                        && f.FiscalPeriod == SecFiscalPeriod.FullYear
                                    )
                                )
                            )
                            .ToList();
                    }

                    if (includeFullYearForQ4)
                    {
                        // Promote per company: only a quarter-span row ending exactly
                        // where the company's own year does is that year's fourth
                        // quarter — comparative re-filings in the slice end earlier
                        // and belong to the prior year. The remaining fp=FY rows
                        // (the annual totals) are dropped, never compared as Q4.
                        facts = facts
                            .Where(f => f.FiscalPeriod == SecFiscalPeriod.Q4)
                            .Concat(
                                facts
                                    .GroupBy(f => f.EquityIssuerId)
                                    .SelectMany(
                                        ReportedQuarterPromotion.PromotedFourthQuartersForYearSlice
                                    )
                            )
                            .ToList();
                    }

                    // Same deterministic pick as GetFinancialFact: canonical periodic
                    // sources win before the alias's primary tag, then latest filing and
                    // accession provide stable amendment ordering (Postgres has no
                    // implicit row order).
                    var best = facts
                        .GroupBy(f => f.EquityIssuerId)
                        .Select(g =>
                        {
                            var (candidates, priority) = BankTopLine.Fold(
                                g.Where(f => conceptPriority.ContainsKey(f.FinancialConceptId))
                                    .ToList(),
                                g.Where(f =>
                                        bankComponentIds.NetInterestIncome.Contains(
                                            f.FinancialConceptId
                                        )
                                    )
                                    .ToList(),
                                g.Where(f =>
                                        bankComponentIds.NoninterestIncome.Contains(
                                            f.FinancialConceptId
                                        )
                                    )
                                    .ToList(),
                                conceptPriority,
                                // One period shows none of the filer's history, so its interest
                                // expense is what proves revenue before interest expense.
                                g.Where(f =>
                                        bankComponentIds.InterestExpense.Contains(
                                            f.FinancialConceptId
                                        )
                                    )
                                    .ToList()
                            );
                            (candidates, priority) = OperatingCostsContinuation.Fold(
                                claimableAlias,
                                operatingCosts.ClaimedStockIds.Contains(g.Key)
                                    ? claimableAlias
                                    : null,
                                candidates,
                                priority,
                                g.Where(f => operatingCosts.Ids.Contains(f.FinancialConceptId))
                                    .ToList()
                            );
                            return (
                                StockId: g.Key,
                                Fact: FinancialFactSeriesShaper.PickBestFact(candidates, priority)
                            );
                        })
                        .Where(item => item.Fact != null)
                        .ToDictionary(item => item.StockId, item => item.Fact);
                    var mistagged =
                        grossProfitComponents.Count == 0
                            ? []
                            : best.Where(entry =>
                                {
                                    var stockFacts = facts
                                        .Where(f => f.EquityIssuerId == entry.Key)
                                        .ToList();
                                    var picks = grossProfitComponents
                                        .Select(c =>
                                            FinancialFactSeriesShaper.PickBestFact(
                                                stockFacts.Where(f =>
                                                    c.Ids.Contains(f.FinancialConceptId)
                                                ),
                                                c.Priority
                                            )
                                        )
                                        .ToList();
                                    return picks.All(pick => pick != null)
                                        && GrossProfitMistag
                                            .MistaggedRows(
                                                [entry.Value],
                                                [picks[0]],
                                                [picks[1]],
                                                [picks[2]]
                                            )
                                            .Count > 0;
                                })
                                .Select(entry => entry.Key)
                                .ToHashSet();
                    return (best, mistagged);
                }

                Dictionary<Guid, FinancialFact> bestByStock;
                HashSet<Guid> mistaggedStockIds;
                if (!calendar)
                {
                    (bestByStock, mistaggedStockIds) = await Slice(fiscalYear);
                }
                else
                {
                    var endingInYear = await Slice(fiscalYear);
                    var endingNextYear = await Slice(fiscalYear + 1);
                    var chosen = CalendarYearAlignment.Choose(
                        fiscalYear,
                        endingInYear.Best,
                        endingNextYear.Best
                    );
                    bestByStock = chosen.ToDictionary(
                        entry => entry.Key,
                        entry => entry.Value.Fact
                    );
                    mistaggedStockIds = chosen
                        .Where(entry =>
                            (
                                entry.Value.Stamp == fiscalYear ? endingInYear : endingNextYear
                            ).Mistagged.Contains(entry.Key)
                        )
                        .Select(entry => entry.Key)
                        .ToHashSet();
                }

                var (rows, skipped) = BuildComparisonRows(requested, stockByTicker, bestByStock);
                var splitAdjustedStockIds = rows.Where(row =>
                        SplitKind(row.Fact, conceptsById) != FactSplitKind.None
                    )
                    .Select(row => row.Fact.EquityIssuerId)
                    .Distinct()
                    .ToList();
                var splitsByStock =
                    splitAdjustedStockIds.Count == 0
                        ? new Dictionary<Guid, List<StockSplit>>()
                        : (
                            await _stockSplitRepository
                                .GetEffective(DateOnly.FromDateTime(DateTime.UtcNow))
                                .Where(split =>
                                    splitAdjustedStockIds.Contains(split.EquityIssuerId)
                                )
                                .ToListAsync()
                        )
                            .GroupBy(split => split.EquityIssuerId)
                            .ToDictionary(group => group.Key, group => group.ToList());

                var (table, capture) = RenderComparison(
                    concept,
                    fiscalYear,
                    period,
                    rows,
                    skipped,
                    splitsByStock,
                    stocks.ToDictionary(
                        stock => stock.Id,
                        stock => stock.Presentation.EquityListingId
                    ),
                    conceptsById,
                    stocks.ToDictionary(stock => stock.Id, EquityListingSymbol.Display),
                    calendar
                        ? stocks.GroupBy(stock => stock.Id).ToDictionary(g => g.Key, g => g.First())
                        : null
                );
                var mistaggedTickers = rows.Where(row =>
                        mistaggedStockIds.Contains(row.Fact.EquityIssuerId)
                    )
                    .Select(row => row.Ticker)
                    .ToList();
                var notes = capture.Notes.ToList();
                if (rows.Any(row => DepreciationTotal.IsSum(row.Fact)))
                {
                    table += SumFootnote;
                    notes.Add(SumFootnoteCardText);
                }
                // A calendar table prints each company's own year label in its Fiscal Year column.
                var ownNaming = calendar ? "" : OwnNamingFootnote(rows, stocks);
                table += ownNaming;
                if (ownNaming.Length > 0)
                    notes.Add(ownNaming.Trim().Trim('_'));
                if (mistaggedTickers.Count > 0)
                {
                    var warning =
                        $"Tag warning: the figure {string.Join(", ", mistaggedTickers)} "
                        + "tagged as gross profit for this period equals its operating income, "
                        + "while Revenue − Cost of Revenue states a different figure";
                    table +=
                        $"\n_{warning}; GetFinancialStatement shows Revenue − Cost of Revenue._\n";
                    notes.Add(warning + ".");
                }
                if (capture.Rows.Count > 0)
                    McpResultCapture.Record(capture with { Notes = notes });
                return table;
            },
            "CompareFinancialFact",
            $"tickers: {FactMarkdown.Clean(string.Join(",", tickers ?? []))}, concept: {FactMarkdown.Clean(concept)}, "
                + $"year: {fiscalYear}, period: {FactMarkdown.Clean(fiscalPeriod)}, basis: {FactMarkdown.Clean(basis)}"
        );
    }

    public Task<string> CompareFinancialFact(
        string tickers,
        string concept,
        int fiscalYear,
        string fiscalPeriod = "FY",
        string basis = null
    ) =>
        CompareFinancialFact(
            // Empty segments are kept, never dropped: a batch like "AAPL,,MSFT" is
            // malformed and must reach ParseComparisonTickers to be reported as an
            // invalid ticker. Removing them here silently narrows the caller's
            // request and answers for fewer companies than were asked for.
            tickers?.Split(',', StringSplitOptions.TrimEntries),
            concept,
            fiscalYear,
            fiscalPeriod,
            basis
        );

    // The year compared is the calendar year each period ends in; a company that names that year
    // for the year it starts in is told apart, so its own documents still read as the same year.
    internal static string OwnNamingFootnote(
        IEnumerable<(string Ticker, string Name, FinancialFact Fact)> rows,
        IEnumerable<EquityIssuer> stocks
    )
    {
        var issuers = stocks.GroupBy(stock => stock.Id).ToDictionary(g => g.Key, g => g.First());
        var named = rows.Where(row =>
                issuers.TryGetValue(row.Fact.EquityIssuerId, out var issuer)
                && FiscalYearLabel.For(row.Fact.FiscalYear, issuer) != row.Fact.FiscalYear
            )
            .Select(row =>
                $"{row.Ticker} names this year FY{FiscalYearLabel.For(row.Fact.FiscalYear, issuers[row.Fact.EquityIssuerId])}"
            )
            .ToList();
        return named.Count == 0 ? "" : $"\n_Own naming: {string.Join("; ", named)}._\n";
    }

    // A summed depreciation and amortization is marked on its own row, so a table that mixes
    // reported totals and sums says which is which; the footnote explains the mark.
    private static string SumMark(FinancialFact fact) =>
        DepreciationTotal.IsSum(fact) ? " " + DepreciationTotal.SumMarker : "";

    private const string SumFootnoteText =
        DepreciationTotal.SumMarker + ": " + DepreciationTotal.SumNote;

    private const string SumFootnote = "\n_" + SumFootnoteText + "_\n";

    private const string SumFootnoteCardText =
        DepreciationTotal.SumMarker
        + " marks depreciation & amortization added up from its reported parts.";

    private const string DerivedMarker = "(derived)";

    private const string DerivedFootnote =
        "\n_"
        + DerivedMarker
        + ": a quarter the filer reports only inside a cumulative figure, worked out from that fiscal "
        + "year's own figures: a second or third quarter as the cumulative figure less the one before "
        + "it, a fourth as the year row less the three quarter rows shown. It has no filing of its own; "
        + "Filed is the latest filing among the figures it comes from._\n";

    private const string DerivedFootnoteCardText =
        "A quarter with no form is worked out from its fiscal year's cumulative figures.";

    private static string RenderFactHistoryTable(
        string concept,
        EquityIssuer stock,
        bool asOriginallyReported,
        List<FinancialFact> perPeriod,
        int totalPeriods,
        string coverageNote,
        IReadOnlyList<StockSplit> splits,
        IReadOnlyDictionary<Guid, FinancialConceptAliases.ConceptRef> conceptsById
    ) =>
        RenderFactHistory(
            concept,
            stock,
            asOriginallyReported,
            perPeriod,
            totalPeriods,
            coverageNote,
            splits,
            conceptsById
        ).Table;

    // The table and the capture of the rows it printed, with the notes under it.
    private static (string Table, FinancialFactHistoryCapture Capture) RenderFactHistory(
        string concept,
        EquityIssuer stock,
        bool asOriginallyReported,
        List<FinancialFact> perPeriod,
        int totalPeriods,
        string coverageNote,
        IReadOnlyList<StockSplit> splits,
        IReadOnlyDictionary<Guid, FinancialConceptAliases.ConceptRef> conceptsById,
        IReadOnlySet<FinancialFact> derived = null
    )
    {
        var rows = new List<FinancialFactCaptureRow>();
        var notes = new List<string>();
        var basis = asOriginallyReported ? "as originally reported" : "latest restated";
        var result = MarkdownTable.Start(
            $"{concept} for {EquityListingSymbol.Display(stock)} ({FactMarkdown.Cell(stock.Name)}) — {basis}:",
            "| Period Start | Period End | FY | Period | Value | Unit | Form | Filed | Accession |",
            "|--------------|------------|---:|--------|------:|------|------|-------|-----------|"
        );
        var splitAdjusted = false;
        var unresolvedBasis = false;
        result.AppendRows(
            perPeriod,
            f =>
            {
                var restated = FactSplitBasis.Restate(
                    f,
                    SplitKind(f, conceptsById),
                    splits,
                    stock.Presentation.EquityListingId
                );
                var value = restated.Value;
                var unresolved = restated.Unresolved;
                unresolvedBasis |= unresolved;
                splitAdjusted |= restated.Adjusted;
                var isDerived = derived?.Contains(f) == true;
                rows.Add(
                    CaptureRow(
                        f,
                        FiscalYearLabel.For(f.FiscalYear, stock),
                        value,
                        unresolved,
                        isDerived
                    )
                );
                return $"| {f.PeriodStart:yyyy-MM-dd} | {f.PeriodEnd:yyyy-MM-dd} | {FiscalYearLabel.For(f.FiscalYear, stock)} | "
                    + $"{f.FiscalPeriod.NameForHumans()} | "
                    + $"{FactMarkdown.Value(value, f.Unit)}{(unresolved ? " (as filed)" : "")}{SumMark(f)}{(isDerived ? " " + DerivedMarker : "")} | "
                    + $"{FactMarkdown.Cell(f.Unit)} | "
                    + $"{FactMarkdown.Cell(f.Form?.DisplayName)} | "
                    + $"{f.FiledDate:yyyy-MM-dd} | "
                    + $"{FactMarkdown.Cell(f.AccessionNumber)} |";
            }
        );

        // The answer prints each note in full; its card takes a short line for its reader.
        void Note(string text, string card)
        {
            result.AppendLine($"\n_{text}_");
            notes.Add(card);
        }
        if (splitAdjusted)
            Note(FinancialFactSplitAdjustment.Note, FinancialFactSplitAdjustment.CardNote);
        if (unresolvedBasis)
            Note(
                FinancialFactSplitAdjustment.UnresolvedNote,
                FinancialFactSplitAdjustment.UnresolvedCardNote
            );
        if (FiscalYearLabel.Note(stock) is { } namingNote)
            Note(namingNote, "Fiscal years are named for the calendar year they start in.");

        // Rows are newest first, so "first N" reads correctly; a no-op empty
        // line when nothing was cut off.
        if (perPeriod.Count < totalPeriods)
        {
            var truncation = McpOutput.TruncationNote(
                perPeriod.Count,
                totalPeriods,
                cap: MaxResultsCap,
                atCapAdvice: "narrow the period range with fromDate/toDate to see older periods"
            );
            result.AppendLine();
            result.AppendLine(truncation);
            // The card states the cut without the answer's advice on arguments, which only a model can act on.
            notes.Add($"Showing the newest {perPeriod.Count} of {totalPeriods} periods.");
        }

        // A card keeps two notes, and a series that stops early is the one its reader most needs told.
        if (coverageNote != null)
        {
            result.AppendLine($"\n_{coverageNote}_");
            notes.Insert(
                0,
                "Recent rows may be missing: the company may have changed how it tags this measure."
            );
        }

        return (
            result.ToString(),
            new FinancialFactHistoryCapture(
                stock,
                concept,
                asOriginallyReported,
                rows,
                totalPeriods,
                notes
            )
        );
    }

    private static FinancialFactCaptureRow CaptureRow(
        FinancialFact fact,
        int fiscalYear,
        decimal value,
        bool asFiled,
        bool derived = false
    ) =>
        new(
            fiscalYear,
            fact.FiscalPeriod,
            fact.PeriodStart,
            fact.PeriodEnd,
            value,
            fact.Unit,
            asFiled,
            DepreciationTotal.IsSum(fact),
            fact.Form?.DisplayName,
            fact.FiledDate,
            derived
        );

    internal static string BuildCoverageNote(
        string ticker,
        DateOnly? aliasLatestPeriodEnd,
        DateOnly? companyLatestPeriodEnd
    )
    {
        if (
            aliasLatestPeriodEnd == null
            || companyLatestPeriodEnd == null
            || companyLatestPeriodEnd.Value.DayNumber - aliasLatestPeriodEnd.Value.DayNumber
                <= MaxUnmarkedSeriesLagDays
        )
            return null;

        return $"Coverage warning: this alias ends {aliasLatestPeriodEnd:yyyy-MM-dd}, while "
            + $"other structured facts for {FactMarkdown.Cell(ticker)} reach "
            + $"{companyLatestPeriodEnd:yyyy-MM-dd}. The filer may have changed XBRL tags; "
            + "missing recent rows do not establish that the measure stopped.";
    }

    private static (
        List<(string Ticker, string Name, FinancialFact Fact)> Rows,
        List<string> Skipped
    ) BuildComparisonRows(
        IReadOnlyList<string> requested,
        IReadOnlyDictionary<string, EquityIssuer> stockByTicker,
        IReadOnlyDictionary<Guid, FinancialFact> bestByStock
    )
    {
        var rows = new List<(string Ticker, string Name, FinancialFact Fact)>();
        var skipped = new List<string>();
        // A company's primary and secondary tickers (GOOGL/GOOG) resolve to the
        // same stock; a second request string for a stock already in the table
        // would duplicate its row, so it is reported instead of re-added.
        var rowTickerByStockId = new Dictionary<Guid, string>();
        foreach (var ticker in requested)
        {
            if (!stockByTicker.TryGetValue(ticker, out EquityIssuer stock))
            {
                skipped.Add(
                    $"{FactMarkdown.Cell(ticker)} (not found in the tracked SEC issuer set)"
                );
                continue;
            }
            if (rowTickerByStockId.TryGetValue(stock.Id, out var firstTicker))
            {
                skipped.Add($"{FactMarkdown.Cell(ticker)} (same company as {firstTicker})");
                continue;
            }
            rowTickerByStockId.Add(stock.Id, ticker);
            if (!bestByStock.TryGetValue(stock.Id, out var best))
            {
                skipped.Add($"{FactMarkdown.Cell(ticker)} (no data)");
                continue;
            }
            // The row stays traceable to the caller's input: a secondary-ticker
            // request shows that ticker, with the primary in parentheses.
            var label =
                ticker == EquityListingSymbol.Display(stock)
                    ? EquityListingSymbol.Display(stock)
                    : $"{ticker} ({EquityListingSymbol.Display(stock)})";
            rows.Add((label, stock.Name, best));
        }
        return (rows, skipped);
    }

    internal static Dictionary<string, EquityIssuer> BuildComparisonStockMap(
        IReadOnlyList<string> requested,
        IReadOnlyList<EquityIssuer> stocks
    )
    {
        var stockByTicker = new Dictionary<string, EquityIssuer>(StringComparer.Ordinal);
        foreach (var ticker in requested)
        {
            EquityIssuer stock = ResolveComparisonStock(stocks, ticker);
            if (stock != null)
                stockByTicker.Add(ticker, stock);
        }
        return stockByTicker;
    }

    internal static (List<string> Tickers, string Error) ParseComparisonTickers(string[] tickers)
    {
        if (tickers == null || tickers.Length == 0)
            return ([], "At least one ticker is required.");

        var segments = tickers.Select(ticker => ticker?.Trim()).ToList();
        if (segments.All(string.IsNullOrWhiteSpace))
            return ([], "At least one ticker is required.");
        if (segments.Count > MaxTickers)
            return ([], $"Too many tickers ({segments.Count}). The maximum is {MaxTickers}.");

        var requested = new List<string>();
        var seenTickers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var segment in segments)
        {
            var normalizedTicker = EquityListingSymbol.TryParse(
                segment,
                out var venueCode,
                out var venueTicker
            )
                ? EquityListingSymbol.Display(venueTicker, null, venueCode)
                : TickerNormalizer.NormalizeListed(segment);
            if (normalizedTicker == null)
                return (
                    [],
                    $"Invalid ticker '{segment}'. Use 1-32 ASCII letters, digits, dots, or dashes."
                );
            if (seenTickers.Add(normalizedTicker))
                requested.Add(normalizedTicker);
        }
        return (requested, null);
    }

    internal static (List<string> Tickers, string Error) ParseComparisonTickers(string tickers) =>
        ParseComparisonTickers(
            // Empty segments are kept, never dropped: a batch like "AAPL,,MSFT" is
            // malformed and must reach ParseComparisonTickers to be reported as an
            // invalid ticker. Removing them here silently narrows the caller's
            // request and answers for fewer companies than were asked for.
            tickers?.Split(',', StringSplitOptions.TrimEntries)
        );

    private static EquityIssuer ResolveComparisonStock(
        IReadOnlyList<EquityIssuer> stocks,
        string ticker
    ) =>
        stocks
            .Where(stock =>
                stock.Presentation.Listing.Ticker == ticker
                || stock
                    .Securities.SelectMany(nativeSecurity => nativeSecurity.Listings)
                    .Where(nativeListing =>
                        nativeListing.MarketCountryCode == "US"
                        && (
                            nativeListing.IsDirectoryListed
                            && nativeListing.Id != stock.Presentation.EquityListingId
                        )
                    )
                    .Select(nativeListing => nativeListing.Ticker)
                    .ToList()
                    .Contains(ticker)
            )
            .OrderBy(stock => stock.Presentation.Listing.Ticker == ticker ? 0 : 1)
            .ThenBy(stock => stock.Presentation.Listing.Ticker, StringComparer.Ordinal)
            .FirstOrDefault();

    // Peer period ends further apart than one calendar quarter mean the rows
    // cover materially different calendar months (NVDA's fiscal Q2 2025 ended
    // July 2024; AMD's ended June 2025) — flag it so the comparison is never
    // read as same-calendar-period.
    private const int MaxAlignedPeriodEndSpreadDays = 92;

    private static string RenderComparisonTable(
        string concept,
        int fiscalYear,
        SecFiscalPeriod period,
        List<(string Ticker, string Name, FinancialFact Fact)> rows,
        List<string> skipped,
        IReadOnlyDictionary<Guid, List<StockSplit>> splitsByStock,
        IReadOnlyDictionary<Guid, Guid> listingIdsByStock,
        IReadOnlyDictionary<Guid, FinancialConceptAliases.ConceptRef> conceptsById
    ) =>
        RenderComparison(
            concept,
            fiscalYear,
            period,
            rows,
            skipped,
            splitsByStock,
            listingIdsByStock,
            conceptsById,
            new Dictionary<Guid, string>()
        ).Table;

    // The table and the capture of the rows it printed, with the notes under it.
    private static (string Table, FinancialFactComparisonCapture Capture) RenderComparison(
        string concept,
        int fiscalYear,
        SecFiscalPeriod period,
        List<(string Ticker, string Name, FinancialFact Fact)> rows,
        List<string> skipped,
        IReadOnlyDictionary<Guid, List<StockSplit>> splitsByStock,
        IReadOnlyDictionary<Guid, Guid> listingIdsByStock,
        IReadOnlyDictionary<Guid, FinancialConceptAliases.ConceptRef> conceptsById,
        IReadOnlyDictionary<Guid, string> symbolsByStock,
        IReadOnlyDictionary<Guid, EquityIssuer> calendarIssuers = null
    )
    {
        var calendar = calendarIssuers != null;
        var captured = new List<FinancialFactComparisonRow>();
        var notes = new List<string>();
        var result = calendar
            ? MarkdownTable.Start(
                $"{concept} — calendar {fiscalYear} peer comparison (each company's fiscal year holding most of its days in {fiscalYear}):",
                "| Ticker | Company | Value | Unit | Fiscal Year | Period | Form | Filed |",
                "|--------|---------|------:|------|-------------|--------|------|-------|"
            )
            : MarkdownTable.Start(
                $"{concept} — {fiscalYear} {period.NameForHumans()} peer comparison:",
                "| Ticker | Company | Value | Unit | Period End | Form | Filed |",
                "|--------|---------|------:|------|-----------|------|-------|"
            );
        var evenSplit = false;
        var splitAdjusted = false;
        var unresolvedBasis = false;
        result.AppendRows(
            rows,
            r =>
            {
                var splits = splitsByStock.GetValueOrDefault(r.Fact.EquityIssuerId) ?? [];
                var restated = FactSplitBasis.Restate(
                    r.Fact,
                    SplitKind(r.Fact, conceptsById),
                    splits,
                    listingIdsByStock[r.Fact.EquityIssuerId]
                );
                var value = restated.Value;
                var unresolved = restated.Unresolved;
                unresolvedBasis |= unresolved;
                splitAdjusted |= restated.Adjusted;
                var ownYear =
                    calendar && calendarIssuers.TryGetValue(r.Fact.EquityIssuerId, out var issuer)
                        ? FiscalYearLabel.For(r.Fact.FiscalYear, issuer)
                        : r.Fact.FiscalYear;
                var even =
                    calendar
                    && CalendarYearAlignment.ShareIn(r.Fact, fiscalYear)
                        < CalendarYearAlignment.EvenSplitShare;
                evenSplit |= even;
                captured.Add(
                    new FinancialFactComparisonRow(
                        r.Ticker,
                        r.Name,
                        CaptureRow(r.Fact, ownYear, value, unresolved),
                        symbolsByStock.GetValueOrDefault(r.Fact.EquityIssuerId),
                        even
                    )
                );
                var periodCells = $"{r.Fact.PeriodEnd:yyyy-MM-dd} | ";
                if (calendar)
                {
                    periodCells =
                        $"FY{ownYear}{(even ? " †" : "")} | "
                        + (
                            r.Fact.PeriodStart == r.Fact.PeriodEnd
                                ? $"at {r.Fact.PeriodEnd:yyyy-MM-dd}"
                                : $"{r.Fact.PeriodStart:yyyy-MM-dd} to {r.Fact.PeriodEnd:yyyy-MM-dd}"
                        )
                        + " | ";
                }
                return $"| {FactMarkdown.Cell(r.Ticker)} | {FactMarkdown.Cell(r.Name)} | "
                    + $"{FactMarkdown.Value(value, r.Fact.Unit)}{(unresolved ? " (as filed)" : "")}{SumMark(r.Fact)} | "
                    + $"{FactMarkdown.Cell(r.Fact.Unit)} | "
                    + periodCells
                    + $"{FactMarkdown.Cell(r.Fact.Form?.DisplayName)} | "
                    + $"{r.Fact.FiledDate:yyyy-MM-dd} |";
            }
        );

        // The answer prints each note in full; its card takes a short line for its reader.
        void Note(string text, string card)
        {
            result.AppendLine($"\n_{text}_");
            notes.Add(card);
        }
        if (splitAdjusted)
            Note(FinancialFactSplitAdjustment.Note, FinancialFactSplitAdjustment.CardNote);
        if (unresolvedBasis)
            Note(
                FinancialFactSplitAdjustment.UnresolvedNote,
                FinancialFactSplitAdjustment.UnresolvedCardNote
            );

        if (rows.Count == 0)
        {
            var none = calendar
                ? $"No company reported '{concept}' for a fiscal year falling mostly in {fiscalYear}."
                : $"No company reported '{concept}' for {fiscalYear} {period.NameForHumans()}.";
            Note(none, none);
        }

        if (evenSplit)
            Note(
                "† This fiscal year splits about evenly between two calendar years; it is listed under the one holding more of its days.",
                "† splits about evenly between two calendar years; listed under the one holding more of its days."
            );

        // A calendar table lines periods up on purpose, so its period ends always span about a year.
        if (rows.Count > 1 && !calendar)
        {
            var earliest = rows.Min(r => r.Fact.PeriodEnd);
            var latest = rows.Max(r => r.Fact.PeriodEnd);
            if (latest.DayNumber - earliest.DayNumber > MaxAlignedPeriodEndSpreadDays)
                Note(
                    $"Note: fiscal calendars differ across these companies — period ends "
                        + $"span {earliest:yyyy-MM-dd} to {latest:yyyy-MM-dd}. Each row covers "
                        + "that company's own fiscal period, not the same calendar months.",
                    $"Fiscal calendars differ: period ends span {earliest:yyyy-MM-dd} to {latest:yyyy-MM-dd}."
                );
        }

        // Listings outside the US report in their own currency, so mixed units are not comparable as written.
        var units = rows.Select(r => r.Fact.Unit)
            .Where(unit => !string.IsNullOrWhiteSpace(unit))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (units.Count > 1)
            Note(
                $"Note: these rows are in different units ({FactMarkdown.Cell(string.Join(", ", units))}); "
                    + "convert them to one currency before comparing values.",
                $"These rows are in different units: {string.Join(", ", units)}."
            );

        if (skipped.Count > 0)
        {
            var skippedNote = $"Skipped: {string.Join(", ", skipped)}.";
            Note(skippedNote, skippedNote);
        }

        return (
            result.ToString(),
            new FinancialFactComparisonCapture(
                concept,
                fiscalYear,
                period,
                captured,
                notes,
                calendar
            )
        );
    }

    // The operating-costs facts the alias claims (OperatingCostsContinuation), with their concept
    // variants; empty when the issuer's history proves no continuation into this alias.
    private async Task<(
        List<FinancialFact> Facts,
        Dictionary<Guid, FinancialConceptAliases.ConceptRef> ConceptsById
    )> LoadOperatingCostsClaimedBy(string alias, IQueryable<FinancialFact> issuerFacts)
    {
        var history = await LoadOperatingCostLines(issuerFacts);
        return
            OperatingCostsContinuation.Claimant(
                history.OperatingCosts,
                history.OperatingExpenses,
                history.TotalCosts
            ) == alias
            ? (history.OperatingCosts, history.OperatingCostsConceptsById)
            : ([], []);
    }

    private async Task<(
        List<FinancialFact> OperatingCosts,
        List<FinancialFact> OperatingExpenses,
        List<FinancialFact> TotalCosts,
        Dictionary<Guid, FinancialConceptAliases.ConceptRef> OperatingCostsConceptsById
    )> LoadOperatingCostLines(IQueryable<FinancialFact> issuerFacts)
    {
        var (operatingCostIds, operatingCostsById) = await ResolveAliasConcepts(
            OperatingCostsContinuation.OperatingCostsAlias
        );
        if (operatingCostIds.Count == 0)
            return ([], [], [], []);
        var (operatingExpenseIds, _) = await ResolveAliasConcepts(
            OperatingCostsContinuation.OperatingExpensesAlias
        );
        var (totalCostIds, _) = await ResolveAliasConcepts(
            OperatingCostsContinuation.TotalCostsAlias
        );
        var ids = operatingCostIds.Concat(operatingExpenseIds).Concat(totalCostIds).ToList();
        var facts = await issuerFacts.Where(f => ids.Contains(f.FinancialConceptId)).ToListAsync();
        return (
            facts.Where(f => operatingCostIds.Contains(f.FinancialConceptId)).ToList(),
            facts.Where(f => operatingExpenseIds.Contains(f.FinancialConceptId)).ToList(),
            facts.Where(f => totalCostIds.Contains(f.FinancialConceptId)).ToList(),
            operatingCostsById
        );
    }

    // The shown gross-profit rows holding operating income, against the issuer's operating income,
    // revenue and cost of revenue picked the same way per fiscal period (GrossProfitMistag).
    private async Task<List<FinancialFact>> MistaggedGrossProfitRows(
        IQueryable<FinancialFact> issuerFacts,
        List<FinancialFact> shown,
        bool asOriginallyReported
    )
    {
        var components = await ResolveGrossProfitComponents();
        if (components.Count == 0)
            return [];

        var ids = components.SelectMany(c => c.Ids).ToList();
        var facts = await issuerFacts.Where(f => ids.Contains(f.FinancialConceptId)).ToListAsync();
        var rows = components
            .Select(c =>
                PickFlowRows(
                    facts.Where(f => c.Ids.Contains(f.FinancialConceptId)).ToList(),
                    c.Priority,
                    asOriginallyReported,
                    formFilter: null
                )
            )
            .ToList();
        return GrossProfitMistag.MistaggedRows(shown, rows[0], rows[1], rows[2]);
    }

    // Per issuer, whether its operating costs continue the alias (OperatingCostsContinuation),
    // from each issuer's whole consolidated history of the three cost lines; the operating-costs
    // variants join conceptsById so a folded row reads its own variant.
    private async Task<(
        HashSet<Guid> Ids,
        HashSet<Guid> ClaimedStockIds
    )> ResolveOperatingCostsClaims(
        string alias,
        IReadOnlyCollection<Guid> stockIds,
        Dictionary<Guid, FinancialConceptAliases.ConceptRef> conceptsById
    )
    {
        var (operatingCostIds, operatingCostsById) = await ResolveAliasConcepts(
            OperatingCostsContinuation.OperatingCostsAlias
        );
        if (operatingCostIds.Count == 0)
            return ([], []);
        var (operatingExpenseIds, _) = await ResolveAliasConcepts(
            OperatingCostsContinuation.OperatingExpensesAlias
        );
        var (totalCostIds, _) = await ResolveAliasConcepts(
            OperatingCostsContinuation.TotalCostsAlias
        );
        var ids = operatingCostIds.Concat(operatingExpenseIds).Concat(totalCostIds).ToList();
        var history = await _financialFactRepository
            .GetConsolidatedByIssuerIds(stockIds)
            .Where(f => ids.Contains(f.FinancialConceptId))
            .ToListAsync();
        var claimed = history
            .GroupBy(f => f.EquityIssuerId)
            .Where(g =>
                OperatingCostsContinuation.Claimant(
                    g.Where(f => operatingCostIds.Contains(f.FinancialConceptId)).ToList(),
                    g.Where(f => operatingExpenseIds.Contains(f.FinancialConceptId)).ToList(),
                    g.Where(f => totalCostIds.Contains(f.FinancialConceptId)).ToList()
                ) == alias
            )
            .Select(g => g.Key)
            .ToHashSet();
        foreach (var (conceptId, conceptRef) in operatingCostsById)
            conceptsById.TryAdd(conceptId, conceptRef);
        return (operatingCostIds, claimed);
    }

    // Operating income, revenue and cost of revenue, in that order: what proves a gross profit
    // holds operating income (GrossProfitMistag). Empty when any is not ingested.
    private async Task<
        List<(HashSet<Guid> Ids, Dictionary<Guid, int> Priority)>
    > ResolveGrossProfitComponents()
    {
        var components = new List<(HashSet<Guid> Ids, Dictionary<Guid, int> Priority)>();
        foreach (
            var alias in new[]
            {
                GrossProfitMistag.OperatingIncomeAlias,
                GrossProfitMistag.RevenueAlias,
                GrossProfitMistag.CostOfRevenueAlias,
            }
        )
        {
            FinancialConceptAliases.TryResolve(alias, out var refs);
            var (priority, _) = await ResolveConcepts(refs);
            if (priority.Count == 0)
                return [];
            components.Add(([.. priority.Keys], priority));
        }
        return components;
    }

    private async Task<(
        HashSet<Guid> Ids,
        Dictionary<Guid, FinancialConceptAliases.ConceptRef> ConceptsById
    )> ResolveAliasConcepts(string alias)
    {
        FinancialConceptAliases.TryResolve(alias, out var refs);
        var (priority, conceptsById) = await ResolveConcepts(refs);
        return ([.. priority.Keys], conceptsById);
    }

    // A bank's net interest and noninterest income facts, which the revenue alias folds in where
    // its revenue line is missing or partial (BankTopLine).
    private async Task<(
        List<FinancialFact> NetInterestIncome,
        List<FinancialFact> NoninterestIncome,
        List<FinancialFact> InterestExpense
    )> LoadBankTopLineComponents(IQueryable<FinancialFact> issuerFacts)
    {
        var (netInterestIds, noninterestIds, interestExpenseIds) =
            await ResolveBankTopLineComponentIds();
        var componentIds = netInterestIds
            .Concat(noninterestIds)
            .Concat(interestExpenseIds)
            .ToList();
        var facts =
            componentIds.Count == 0
                ? []
                : await issuerFacts
                    .Where(f => componentIds.Contains(f.FinancialConceptId))
                    .ToListAsync();
        return (
            facts.Where(f => netInterestIds.Contains(f.FinancialConceptId)).ToList(),
            facts.Where(f => noninterestIds.Contains(f.FinancialConceptId)).ToList(),
            facts.Where(f => interestExpenseIds.Contains(f.FinancialConceptId)).ToList()
        );
    }

    private async Task<(
        HashSet<Guid> NetInterestIncome,
        HashSet<Guid> NoninterestIncome,
        HashSet<Guid> InterestExpense
    )> ResolveBankTopLineComponentIds()
    {
        FinancialConceptAliases.TryResolve(BankTopLine.NetInterestIncomeAlias, out var netInterest);
        FinancialConceptAliases.TryResolve(BankTopLine.NoninterestIncomeAlias, out var noninterest);
        FinancialConceptAliases.TryResolve(
            BankTopLine.InterestExpenseAlias,
            out var interestExpense
        );
        var (netInterestPriority, _) = await ResolveConcepts(netInterest);
        var (noninterestPriority, _) = await ResolveConcepts(noninterest);
        var (interestExpensePriority, _) = await ResolveConcepts(interestExpense);
        return (
            [.. netInterestPriority.Keys],
            [.. noninterestPriority.Keys],
            [.. interestExpensePriority.Keys]
        );
    }

    // The alias's ingested concepts: their pick priority, and the variant each was reported
    // under, which decides how a split moves its values.
    private async Task<(
        Dictionary<Guid, int> Priority,
        Dictionary<Guid, FinancialConceptAliases.ConceptRef> ConceptsById
    )> ResolveConcepts(IReadOnlyList<FinancialConceptAliases.ConceptRef> conceptRefs)
    {
        var taxonomies = conceptRefs.Select(c => c.Taxonomy).Distinct().ToList();
        var tags = conceptRefs.Select(c => c.Tag).Distinct().ToList();
        // (taxonomy, tag) → position in the alias's ordered tag list; lower
        // index is the alias's preferred/primary concept.
        var priorityByPair = new Dictionary<(FactTaxonomy, string), int>();
        for (var i = 0; i < conceptRefs.Count; i++)
            priorityByPair.TryAdd((conceptRefs[i].Taxonomy, conceptRefs[i].Tag), i);

        var rows = await _financialConceptRepository
            .GetMatching(taxonomies, tags)
            .Select(c => new
            {
                c.Id,
                c.Taxonomy,
                c.Tag,
            })
            .ToListAsync();

        // GetMatching is a taxonomy×tag cross product over a tiny alias tag set
        // (over-fetch is negligible here); narrow to the alias's exact pairs.
        var known = rows.Where(r => priorityByPair.ContainsKey((r.Taxonomy, r.Tag))).ToList();
        return (
            known.ToDictionary(r => r.Id, r => priorityByPair[(r.Taxonomy, r.Tag)]),
            known.ToDictionary(r => r.Id, r => conceptRefs[priorityByPair[(r.Taxonomy, r.Tag)]])
        );
    }

    private static FactSplitKind SplitKind(
        FinancialFact fact,
        IReadOnlyDictionary<Guid, FinancialConceptAliases.ConceptRef> conceptsById
    ) =>
        FactSplitBasis.Classify(conceptsById.GetValueOrDefault(fact.FinancialConceptId), fact.Unit);

    // Accepts an absent bound (null/blank → no bound) but rejects a non-empty
    // value that is not an ISO yyyy-MM-dd date, so a typo can't silently widen
    // the result set.
    private static bool TryParseBound(string value, out DateOnly? bound)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            bound = null;
            return true;
        }

        if (
            DateOnly.TryParseExact(
                value.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed
            )
        )
        {
            bound = parsed;
            return true;
        }

        bound = null;
        return false;
    }

    private static string SupportedAliasesNote()
    {
        return "Supported: "
            + $"{string.Join(", ", FinancialConceptAliases.SupportedAliases)} "
            + "(common synonyms like 'sales', 'r&d', 'ocf' also work).";
    }
}
