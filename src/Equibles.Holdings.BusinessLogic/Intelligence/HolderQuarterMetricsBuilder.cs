using Equibles.CorporateActions.Data;
using Equibles.Holdings.BusinessLogic.Intelligence.Benchmarks;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence;

/// <summary>
/// Assembles one filer's <see cref="HolderQuarterlyMetrics"/> for a covered quarter from its 13F
/// rows, restating the quarter before onto this quarter's share basis, and reports the presence
/// bits for the quarter that must change.
/// </summary>
public static class HolderQuarterMetricsBuilder
{
    public static HolderQuarterResult Build(HolderQuarterInput input, int calculationVersion)
    {
        var quarter =
            HolderTenureCalculator.QuarterIndex(input.ReportDate)
            ?? throw new ArgumentException(
                $"{input.ReportDate:yyyy-MM-dd} is not a covered quarter end",
                nameof(input)
            );
        var current = EquityPositions(input.Current);
        var unresolved = new HashSet<HolderSecurityKey>();
        var previous =
            input.Previous == null
                ? null
                : Restate(EquityPositions(input.Previous), input.PreviousShareFactors, unresolved);

        var trades = HolderTurnoverCalculator.Trades(
            current,
            previous,
            input.AveragePrices,
            unresolved
        );
        var concentration = HolderConcentrationCalculator.Measure(current);
        var issuerValues = HolderConcentrationCalculator.IssuerValues(current);
        var benchmarks = MeasureBenchmarks(input, issuerValues);
        // Benchmarks arrive in catalog order, so a tie keeps the published index. A benchmark too
        // little of which resolved to issuers is reported but never decides the band.
        var eligible = benchmarks
            .Where(row => row.Benchmark.Coverage >= input.MinimumBenchmarkCoverage)
            .ToList();
        (HolderBenchmark Benchmark, ActiveShareMeasurement Measurement)? closest =
            eligible.Count == 0 ? null : eligible.MinBy(row => row.Measurement.ActiveShare);
        var sectorActiveShare = ActiveShareCalculator.ActiveShare(
            HolderSectorWeights.Of(issuerValues, input.IssuerSectors),
            input.AggregateSectorWeights
        );

        var bit = HolderTenureCalculator.Set(0, quarter);
        var filed = input.FiledMask | bit;
        var held = current
            .Where(position => position.Shares > 0)
            .GroupBy(position => position.Security)
            .ToDictionary(
                group => group.Key,
                group =>
                    group.All(position => position.Value != null)
                        ? group.Sum(position => (decimal)position.Value.GetValueOrDefault())
                        : 0m
            );
        var toSet = new List<HolderSecurityKey>();
        var tenures = new List<(decimal Value, HolderTenure Tenure)>();
        var tenureBySecurity = new Dictionary<HolderSecurityKey, HolderTenure>(held.Count);
        foreach (var (security, value) in held)
        {
            var mask = input.Presence.GetValueOrDefault(security);
            if ((mask & bit) == 0)
                toSet.Add(security);
            // The quarter's own bit is set, so a held security always has a tenure.
            if (
                HolderTenureCalculator.Tenure(
                    mask | bit,
                    filed,
                    quarter,
                    input.FirstCoveredQuarter
                ) is
                { } run
            )
            {
                tenures.Add((value, run));
                tenureBySecurity[security] = run;
            }
        }
        var toClear = input
            .Presence.Where(pair => (pair.Value & bit) != 0 && !held.ContainsKey(pair.Key))
            .Select(pair => pair.Key)
            .ToList();
        var tenure = HolderTenureCalculator.Summarize(tenures);

        var trailing = HolderTurnoverCalculator.TrailingAnnual([
            .. input.PriorTurnovers,
            trades.Turnover,
        ]);
        var style = HolderStyleBandClassifier.Classify(
            new HolderStyleInputs
            {
                EquityAum = trades.EquityAum,
                StockCount = concentration.StockCount,
                ClosestActiveShare = closest?.Measurement.ActiveShare,
                TrailingAnnualTurnover = trailing,
                TopTenWeight = concentration.TopTenWeight,
                EffectivePositions = concentration.EffectivePositions,
                ValueWeightedTenureQuarters = tenure.ValueWeightedQuarters,
                CensoredValueShare = tenure.CensoredValueShare,
                IndexSeriesNetAssets = input.IndexSeriesNetAssets,
                AdvisedSeriesNetAssets = input.AdvisedSeriesNetAssets,
            }
        );

        var metrics = new HolderQuarterlyMetrics
        {
            InstitutionalHolderId = input.InstitutionalHolderId,
            ReportDate = input.ReportDate,
            EquityAum = trades.EquityAum,
            PreviousEquityAum = trades.PreviousEquityAum,
            StockCount = concentration.StockCount,
            BuyValue = trades.BuyValue,
            SellValue = trades.SellValue,
            Turnover = trades.Turnover,
            TrailingAnnualTurnover = trailing,
            NewCount = trades.NewCount,
            ExitCount = trades.ExitCount,
            IncreasedCount = trades.IncreasedCount,
            ReducedCount = trades.ReducedCount,
            UnpricedTradeCount = trades.UnpricedTradeCount,
            TopTenWeight = concentration.TopTenWeight,
            Herfindahl = concentration.Herfindahl,
            EffectivePositions = concentration.EffectivePositions,
            ValueWeightedTenureQuarters = tenure.ValueWeightedQuarters,
            CensoredValueShare = tenure.CensoredValueShare,
            AggregateActiveShare = benchmarks
                .Where(row => row.Benchmark.Key == HolderBenchmarkCatalog.Aggregate)
                .Select(row => (decimal?)row.Measurement.ActiveShare)
                .FirstOrDefault(),
            ClosestBenchmark = closest?.Benchmark.Key,
            ClosestBenchmarkName = HolderBenchmarkCatalog.Name(closest?.Benchmark.Key),
            ClosestBenchmarkAsOf = closest?.Benchmark.AsOf,
            ClosestActiveShare = closest?.Measurement.ActiveShare,
            SectorActiveShare = sectorActiveShare,
            ComparedHolderId =
                input.Previous == null
                    ? null
                    : input.ComparedHolderId ?? input.InstitutionalHolderId,
            IndexSeriesNetAssets = input.IndexSeriesNetAssets,
            AdvisedSeriesNetAssets = input.AdvisedSeriesNetAssets,
            AdvisedSeriesAsOf = input.AdvisedSeriesAsOf,
            CallValue = LegValue(input.Current, row => row.OptionType == OptionType.Call),
            PutValue = LegValue(input.Current, row => row.OptionType == OptionType.Put),
            PrincipalValue = LegValue(
                input.Current,
                row => row.OptionType == null && row.ShareType == ShareType.Principal
            ),
            StyleBand = style.Style,
            IndexLikeEvidence = style.IndexLikeEvidence,
            TurnoverBand = style.TurnoverBand,
            HorizonBand = style.HorizonBand,
            BandVersion = HolderStyleBandClassifier.BandVersion,
            CalculationVersion = calculationVersion,
        };
        var sectorFlows = HolderSectorFlowCalculator.Flows(
            input.InstitutionalHolderId,
            input.ReportDate,
            trades.SecurityTrades,
            input.IssuerSectors,
            input.ExchangeTradedSecurities
        );
        var benchmarkRows = benchmarks
            .Select(row => new HolderQuarterlyBenchmarkActiveShare
            {
                InstitutionalHolderId = input.InstitutionalHolderId,
                ReportDate = input.ReportDate,
                BenchmarkKey = row.Benchmark.Key,
                BenchmarkAsOf = row.Benchmark.AsOf,
                ActiveShare = row.Measurement.ActiveShare,
                Overlap = row.Measurement.Overlap,
                Coverage = Math.Clamp(row.Benchmark.Coverage, 0, 1),
            })
            .ToList();
        var costSlots = HolderCostBasisCalculator.Advance(
            current,
            previous,
            input.PreviousCostSlots,
            input.PreviousShareFactors,
            unresolved,
            input.AveragePrices
        );
        var book = new HolderQuarterBook
        {
            Current = current,
            Previous = previous,
            UnresolvedBasis = unresolved,
            Tenures = tenureBySecurity,
            CostSlots = costSlots,
            SecurityTrades = trades.SecurityTrades,
        };
        return new HolderQuarterResult(metrics, toSet, toClear, sectorFlows, benchmarkRows, book);
    }

    // Every benchmark the book can be measured against; an empty book measures none. One pass
    // over the book's issuers per benchmark, so a benchmark's size never multiplies the work.
    private static List<(
        HolderBenchmark Benchmark,
        ActiveShareMeasurement Measurement
    )> MeasureBenchmarks(HolderQuarterInput input, IReadOnlyDictionary<Guid, decimal> issuerValues)
    {
        var measured = new List<(HolderBenchmark, ActiveShareMeasurement)>();
        foreach (var benchmark in input.Benchmarks)
            if (ActiveShareCalculator.Measure(issuerValues, benchmark.Weights) is { } measurement)
                measured.Add((benchmark, measurement));
        return measured;
    }

    // Common shares only: put/call and principal legs never count as the filer's equity book.
    private static List<HolderEquityPosition> EquityPositions(
        IReadOnlyList<HolderIntelligenceRow> rows
    ) =>
        rows.Where(row => row.ShareType == ShareType.Shares && row.OptionType == null)
            .Select(row => new HolderEquityPosition(
                row.Security,
                row.Shares,
                row.ValueMissing ? null : row.Value,
                row.FiledValue
            ))
            .ToList();

    private static List<HolderEquityPosition> Restate(
        List<HolderEquityPosition> previous,
        IReadOnlyDictionary<HolderSecurityKey, decimal?> factors,
        HashSet<HolderSecurityKey> unresolved
    ) =>
        previous
            .Select(position =>
            {
                if (!factors.TryGetValue(position.Security, out var factor))
                    return position;
                if (factor is not { } known)
                {
                    unresolved.Add(position.Security);
                    return position;
                }
                return position with
                {
                    Shares = SplitAdjustment.AdjustShareCount(position.Shares, known),
                };
            })
            .ToList();

    private static long LegValue(
        IReadOnlyList<HolderIntelligenceRow> rows,
        Func<HolderIntelligenceRow, bool> leg
    ) => rows.Where(row => leg(row) && !row.ValueMissing).Sum(row => row.Value);
}
