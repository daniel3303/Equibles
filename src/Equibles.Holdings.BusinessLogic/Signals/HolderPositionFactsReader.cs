using Equibles.Core.AutoWiring;
using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.BusinessLogic.Signals.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.BusinessLogic.Signals;

/// <summary>
/// How long a 13F filer has held each of its positions and their estimated cost per share at a
/// closed quarter, read from the presence masks and cost slots the holder-intelligence drain keeps.
/// A quarter the drain has not computed yet answers nothing, never a zero.
/// </summary>
[Service]
public class HolderPositionFactsReader
{
    private readonly HolderSignalsRepository _signalsRepository;
    private readonly InstitutionalHolderRepository _holderRepository;
    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly HoldingsCorpusCoverage _corpusCoverage;

    public HolderPositionFactsReader(
        HolderSignalsRepository signalsRepository,
        InstitutionalHolderRepository holderRepository,
        InstitutionalHoldingRepository holdingRepository,
        HoldingsCorpusCoverage corpusCoverage
    )
    {
        _signalsRepository = signalsRepository;
        _holderRepository = holderRepository;
        _holdingRepository = holdingRepository;
        _corpusCoverage = corpusCoverage;
    }

    /// <summary>
    /// The facts of the filer's positions in <paramref name="securities"/> at
    /// <paramref name="reportDate"/>; an empty map when the quarter is not computed.
    /// </summary>
    public async Task<Dictionary<HolderSecurityKey, HolderPositionFacts>> Read(
        InstitutionalHolder holder,
        DateOnly reportDate,
        IReadOnlyCollection<HolderSecurityKey> securities,
        CancellationToken cancellationToken = default
    )
    {
        var facts = new Dictionary<HolderSecurityKey, HolderPositionFacts>();
        var quarter = HolderTenureCalculator.QuarterIndex(reportDate);
        if (quarter == null || securities.Count == 0)
            return facts;
        var state = await _holderRepository
            .GetComputedIntelligenceQuarters()
            .AsNoTracking()
            .Where(state => state.ReportDate == reportDate)
            .Select(state => new { state.CalculationVersion })
            .FirstOrDefaultAsync(cancellationToken);
        if (state == null)
            return facts;
        var costed = state.CalculationVersion >= HolderCostBasisCalculator.FirstVersion;

        var issuerIds = securities.Select(security => security.IssuerId).Distinct().ToList();
        var presence = await _signalsRepository
            .GetPresence(holder.Id)
            .AsNoTracking()
            .Where(row => issuerIds.Contains(row.EquityIssuerId))
            .Select(row => new
            {
                row.EquityIssuerId,
                row.ListedTicker,
                row.HeldMask,
            })
            .ToListAsync(cancellationToken);
        // The quarters the filer filed a 13F at all, as the drain reads them for tenure.
        var filed = await _holdingRepository.Get13FReportDatesByHolderSnapshotBacked(
            holder,
            cancellationToken
        );
        var filedMask = filed.Aggregate(
            0L,
            (mask, date) =>
                HolderTenureCalculator.QuarterIndex(date) is { } index
                    ? HolderTenureCalculator.Set(mask, index)
                    : mask
        );
        var bit = 1L << quarter.Value;
        var firstCovered = HolderTenureCalculator.QuarterIndex(
            HoldingsCorpusCoverage.FirstQuarterEndOnOrAfter(
                _corpusCoverage.CoverageStartDate > HolderTenureCalculator.FirstQuarter
                    ? _corpusCoverage.CoverageStartDate
                    : HolderTenureCalculator.FirstQuarter
            )
        );
        var costs = costed
            ? (
                await _signalsRepository
                    .GetCostBasis(holder.Id)
                    .AsNoTracking()
                    .Where(row => issuerIds.Contains(row.EquityIssuerId))
                    .Select(row => new
                    {
                        row.EquityIssuerId,
                        row.ListedTicker,
                        Cost = row.CostPerShare.Length > quarter.Value
                            ? row.CostPerShare[quarter.Value]
                            : 0f,
                        Unpriced = (row.UnpricedMask & bit) != 0,
                    })
                    .ToListAsync(cancellationToken)
            ).ToDictionary(row => new HolderSecurityKey(row.EquityIssuerId, row.ListedTicker))
            : null;

        var presenceByKey = presence.ToDictionary(row => new HolderSecurityKey(
            row.EquityIssuerId,
            row.ListedTicker
        ));
        foreach (var security in securities.Distinct())
        {
            var held = presenceByKey.TryGetValue(security, out var row) ? row.HeldMask : 0L;
            var tenure =
                firstCovered is { } first && quarter.Value >= first
                    ? HolderTenureCalculator.Tenure(held, filedMask | bit, quarter.Value, first)
                    : null;
            HolderCostStatus? status = null;
            decimal? cost = null;
            if (costs != null && tenure != null)
            {
                if (costs.TryGetValue(security, out var slot) && slot.Cost > 0)
                {
                    status = HolderCostStatus.Estimated;
                    cost = (decimal)slot.Cost;
                }
                else
                    status = slot is { Unpriced: true }
                        ? HolderCostStatus.Unpriced
                        : HolderCostStatus.UnknownEntry;
            }
            facts[security] = new HolderPositionFacts
            {
                HeldQuarters = tenure?.Quarters,
                HeldSinceCoverageStart = tenure?.Censored ?? false,
                CostStatus = status,
                EstimatedCostPerShare = cost,
            };
        }
        return facts;
    }
}
