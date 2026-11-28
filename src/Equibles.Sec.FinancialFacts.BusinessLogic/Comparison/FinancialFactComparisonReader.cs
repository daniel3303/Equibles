using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.Comparison;

// One concept compared across companies for one period: each company's pick, with the folds and readings a
// single-company read applies (bank top line, operating costs, depreciation total, continuing-operations EPS, owners'
// profit sign, a host's own net income). Calendar lines fiscal years up by the calendar year holding most of their days.
// Shared by the MCP CompareFinancialFact tool and the REST compare route, so both answer with the same figures.
[Service]
public class FinancialFactComparisonReader
{
    private readonly StatementFactRepository _financialFactRepository;
    private readonly FinancialConceptRepository _financialConceptRepository;
    private readonly FinancialConceptResolver _concepts;
    private readonly INetIncomeFactsReader _netIncomeFactsReader;

    // The net income reader is optional: a host without one reads every issuer's filed net income.
    public FinancialFactComparisonReader(
        StatementFactRepository financialFactRepository,
        FinancialConceptRepository financialConceptRepository,
        INetIncomeFactsReader netIncomeFactsReader = null
    )
    {
        _financialFactRepository = financialFactRepository;
        _financialConceptRepository = financialConceptRepository;
        _concepts = new FinancialConceptResolver(
            financialFactRepository,
            financialConceptRepository
        );
        _netIncomeFactsReader = netIncomeFactsReader;
    }

    // Null when the concept has no ingested data. An issuer without a figure for the period has no entry.
    public async Task<FinancialFactComparison> Compare(
        IReadOnlyList<FinancialConceptAliases.ConceptRef> conceptRefs,
        IEnumerable<EquityIssuer> issuers,
        int fiscalYear,
        SecFiscalPeriod period,
        bool calendar
    )
    {
        var (conceptPriority, conceptsById) = await _concepts.ResolveConcepts(conceptRefs);
        if (conceptPriority.Count == 0)
            return null;
        var stocks = issuers.DistinctBy(issuer => issuer.Id).ToList();

        var stockIds = stocks.Select(s => s.Id).ToList();
        // The revenue alias also loads a bank's net interest and noninterest income,
        // which stand in for a missing or partial revenue line (BankTopLine).
        var foldsRevenue = BankTopLine.IsRevenue(conceptRefs);
        var bankComponentIds = foldsRevenue
            ? await _concepts.ResolveBankTopLineComponentIds()
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
                : await _concepts.ResolveOperatingCostsClaims(
                    claimableAlias,
                    stockIds,
                    conceptsById
                );
        var grossProfitComponents = GrossProfitMistag.IsGrossProfit(conceptRefs)
            ? await _concepts.ResolveGrossProfitComponents()
            : [];
        // Net income compares the line a host proves for each issuer it reads its own way
        // (INetIncomeFactsReader), as GetFinancialFact reads it.
        var hostNetIncome = new Dictionary<Guid, NetIncomeReading>();
        if (OwnersProfitSign.IsNetIncome(conceptRefs))
        {
            var readings = new List<(EquityIssuer Stock, NetIncomeReading Reading)>();
            foreach (var stock in stocks)
            {
                readings.Add((stock, await ReadHostNetIncome(stock)));
            }
            foreach (var (stock, reading) in readings.Where(read => read.Reading != null))
            {
                hostNetIncome[stock.Id] = reading;
                foreach (var (conceptId, conceptRef) in reading.Concepts)
                {
                    conceptsById.TryAdd(conceptId, conceptRef);
                    conceptPriority.TryAdd(conceptId, 0);
                }
            }
        }
        // One fiscal year's pick per company, and the companies whose gross profit that pick proves
        // mis-tagged. A calendar comparison runs it for the two years that can hold the calendar year.
        async Task<(Dictionary<Guid, FinancialFact> Best, HashSet<Guid> Mistagged)> Slice(int year)
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
                        || (includeFullYearForQ4 && f.FiscalPeriod == SecFiscalPeriod.FullYear)
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
                            || (includeFullYearForQ4 && f.FiscalPeriod == SecFiscalPeriod.FullYear)
                        )
                    )
                    .ToList();
            }
            // Diluted EPS is judged on each issuer's whole history too, as GetFinancialFact
            // reads it (ContinuingOperationsEps).
            else if (ContinuingOperationsEps.IsFamily(conceptRefs))
            {
                var eps = await ContinuingOperationsEpsFacts.Load(
                    _financialFactRepository,
                    _financialConceptRepository,
                    stockIds
                );
                conceptPriority = eps.Priority;
                foreach (var (conceptId, conceptRef) in eps.ConceptsById)
                    conceptsById.TryAdd(conceptId, conceptRef);
                facts = eps
                    .Facts.Where(f =>
                        f.FiscalYear == year
                        && (
                            f.FiscalPeriod == period
                            || (includeFullYearForQ4 && f.FiscalPeriod == SecFiscalPeriod.FullYear)
                        )
                    )
                    .ToList();
            }

            // A foreign filer's owners' profit reads the sign its filing proves, as
            // GetFinancialFact reads it (OwnersProfitSign).
            if (OwnersProfitSign.IsNetIncome(conceptRefs))
                facts = (
                    await OwnersProfitSignFacts.Read(
                        _financialFactRepository,
                        _financialConceptRepository,
                        stockIds,
                        facts,
                        conceptPriority.Keys.ToHashSet()
                    )
                ).Facts;
            if (hostNetIncome.Count > 0)
                facts =
                [
                    .. facts.Where(f => !hostNetIncome.ContainsKey(f.EquityIssuerId)),
                    // Filed figures only, as for every other issuer compared.
                    .. hostNetIncome.Values.SelectMany(reading =>
                        reading.Facts.Where(f =>
                            !reading.Derived.Contains(f)
                            && f.FiscalYear == year
                            && (
                                f.FiscalPeriod == period
                                || (
                                    includeFullYearForQ4
                                    && f.FiscalPeriod == SecFiscalPeriod.FullYear
                                )
                            )
                        )
                    ),
                ];

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
                            .SelectMany(ReportedQuarterPromotion.PromotedFourthQuartersForYearSlice)
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
                    var own = g.Where(f => conceptPriority.ContainsKey(f.FinancialConceptId))
                        .ToList();
                    // Only revenue folds: the fold also drops revenue slices (RevenueSlices),
                    // which no other concept's tags are.
                    var (candidates, priority) = !foldsRevenue
                        ? (own, new Dictionary<Guid, int>(conceptPriority))
                        : BankTopLine.Fold(
                            own,
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
                                    bankComponentIds.InterestExpense.Contains(f.FinancialConceptId)
                                )
                                .ToList()
                        );
                    (candidates, priority) = OperatingCostsContinuation.Fold(
                        claimableAlias,
                        operatingCosts.ClaimedStockIds.Contains(g.Key) ? claimableAlias : null,
                        candidates,
                        priority,
                        g.Where(f => operatingCosts.Ids.Contains(f.FinancialConceptId)).ToList()
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
                                        stockFacts.Where(f => c.Ids.Contains(f.FinancialConceptId)),
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
            bestByStock = chosen.ToDictionary(entry => entry.Key, entry => entry.Value.Fact);
            mistaggedStockIds = chosen
                .Where(entry =>
                    (
                        entry.Value.Stamp == fiscalYear ? endingInYear : endingNextYear
                    ).Mistagged.Contains(entry.Key)
                )
                .Select(entry => entry.Key)
                .ToHashSet();
        }

        return new FinancialFactComparison(bestByStock, mistaggedStockIds, conceptsById);
    }

    private Task<NetIncomeReading> ReadHostNetIncome(EquityIssuer stock) =>
        _netIncomeFactsReader == null
            ? Task.FromResult<NetIncomeReading>(null)
            : _netIncomeFactsReader.Read(stock);
}
