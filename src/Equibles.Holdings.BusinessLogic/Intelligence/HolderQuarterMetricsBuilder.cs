using Equibles.CorporateActions.Data;
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

        var trades = HolderTurnoverCalculator.Trades(current, previous, null, unresolved);
        var concentration = HolderConcentrationCalculator.Measure(current);
        var activeShare = ActiveShareCalculator.ActiveShare(
            HolderConcentrationCalculator.IssuerValues(current),
            input.AggregateBenchmark
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
                        ? group.Sum(position => (decimal)position.Value.Value)
                        : 0m
            );
        var toSet = new List<HolderSecurityKey>();
        var tenures = new List<(decimal Value, HolderTenure Tenure)>();
        foreach (var (security, value) in held)
        {
            var mask = input.Presence.GetValueOrDefault(security);
            if ((mask & bit) == 0)
                toSet.Add(security);
            var run = HolderTenureCalculator.Tenure(
                mask | bit,
                filed,
                quarter,
                input.FirstCoveredQuarter
            );
            tenures.Add((value, run.Value));
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
                ClosestActiveShare = activeShare,
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
            AggregateActiveShare = activeShare,
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
        return new HolderQuarterResult(metrics, toSet, toClear);
    }

    // Common shares only: put/call and principal legs never count as the filer's equity book.
    private static List<HolderEquityPosition> EquityPositions(
        IReadOnlyList<HolderIntelligenceRow> rows
    ) =>
        rows.Where(row => row.ShareType == ShareType.Shares && row.OptionType == null)
            .Select(row => new HolderEquityPosition(
                row.Security,
                row.Shares,
                row.ValueMissing ? null : row.Value
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
