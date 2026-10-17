using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.UnitTests.Holdings.Intelligence;

public class HolderQuarterMetricsBuilderTests
{
    private static readonly DateOnly Quarter = new(2024, 12, 31);
    private static readonly DateOnly Previous = new(2024, 9, 30);
    private static readonly Guid Holder = Guid.NewGuid();
    private static readonly Guid Kept = Guid.NewGuid();
    private static readonly Guid Split = Guid.NewGuid();
    private static readonly Guid Dropped = Guid.NewGuid();

    [Fact]
    public void Build_KeepsLegsOutOfTheEquityBookAndRestatesTheQuarterBefore()
    {
        var input = new HolderQuarterInput
        {
            InstitutionalHolderId = Holder,
            ReportDate = Quarter,
            Current =
            [
                Row(Kept, Quarter, 100, 1_000),
                Row(Split, Quarter, 200, 2_000),
                Row(Kept, Quarter, 50, 300, optionType: OptionType.Call),
                Row(Kept, Quarter, 40, 200, optionType: OptionType.Put),
                Row(Dropped, Quarter, 10, 999, shareType: ShareType.Principal),
                Row(Guid.NewGuid(), Quarter, 5, 777, valueMissing: true),
            ],
            Previous = [Row(Kept, Previous, 100, 900), Row(Split, Previous, 100, 1_800)],
            // A 2-for-1 split inside the quarter: 100 old shares are 200 new ones, so no trade.
            PreviousShareFactors = new Dictionary<HolderSecurityKey, decimal?>
            {
                [new(Split, null)] = 2m,
            },
            AggregateBenchmark = new Dictionary<Guid, decimal> { [Kept] = 1, [Split] = 2 },
        };

        var metrics = HolderQuarterMetricsBuilder.Build(input, 7).Metrics;

        metrics.InstitutionalHolderId.Should().Be(Holder);
        metrics.EquityAum.Should().Be(3_000);
        metrics.PreviousEquityAum.Should().Be(2_700);
        metrics.StockCount.Should().Be(3);
        metrics.NewCount.Should().Be(1);
        metrics.IncreasedCount.Should().Be(0);
        metrics.UnpricedTradeCount.Should().Be(1);
        metrics.Turnover.Should().Be(0m);
        metrics.CallValue.Should().Be(300);
        metrics.PutValue.Should().Be(200);
        metrics.PrincipalValue.Should().Be(999);
        metrics.AggregateActiveShare.Should().BeApproximately(0m, 0.0000001m);
        metrics.CalculationVersion.Should().Be(7);
        metrics.BandVersion.Should().Be(HolderStyleBandClassifier.BandVersion);
    }

    [Fact]
    public void Build_UnresolvedSplitBasis_LeavesTheTradeUnpriced()
    {
        var input = new HolderQuarterInput
        {
            InstitutionalHolderId = Holder,
            ReportDate = Quarter,
            Current = [Row(Split, Quarter, 300, 3_000)],
            Previous = [Row(Split, Previous, 100, 1_000)],
            PreviousShareFactors = new Dictionary<HolderSecurityKey, decimal?>
            {
                [new(Split, null)] = null,
            },
        };

        var metrics = HolderQuarterMetricsBuilder.Build(input, 1).Metrics;

        metrics.IncreasedCount.Should().Be(0);
        metrics.UnpricedTradeCount.Should().Be(1);
        metrics.BuyValue.Should().Be(0m);
    }

    [Fact]
    public void Build_NoFilingTheQuarterBefore_LeavesTradesAndTrailingTurnoverUnknown()
    {
        var metrics = HolderQuarterMetricsBuilder
            .Build(
                new HolderQuarterInput
                {
                    InstitutionalHolderId = Holder,
                    ReportDate = Quarter,
                    Current = [Row(Kept, Quarter, 100, 1_000)],
                    PriorTurnovers = [0.1m, 0.1m, 0.1m],
                },
                1
            )
            .Metrics;

        metrics.Turnover.Should().BeNull();
        metrics.NewCount.Should().BeNull();
        metrics.TrailingAnnualTurnover.Should().BeNull();
    }

    [Fact]
    public void Build_AddsThisQuarterToTheThreeBeforeForTrailingTurnover()
    {
        var metrics = HolderQuarterMetricsBuilder
            .Build(
                new HolderQuarterInput
                {
                    InstitutionalHolderId = Holder,
                    ReportDate = Quarter,
                    Current = [Row(Kept, Quarter, 100, 1_000), Row(Split, Quarter, 100, 1_000)],
                    Previous =
                    [
                        Row(Kept, Previous, 100, 1_000),
                        Row(Dropped, Previous, 100, 1_000),
                    ],
                    PriorTurnovers = [0.1m, 0.2m, 0.3m],
                },
                1
            )
            .Metrics;

        metrics.Turnover.Should().Be(0.5m);
        metrics.TrailingAnnualTurnover.Should().Be(1.1m);
        metrics.TurnoverBand.Should().Be(HolderTurnoverBand.High);
    }

    [Fact]
    public void Build_ReadsTenureFromStoredMasksAndReportsTheBitsToChange()
    {
        var quarter = HolderTenureCalculator.QuarterIndex(Quarter).Value;
        var stale = new HolderSecurityKey(Dropped, null);
        var input = new HolderQuarterInput
        {
            InstitutionalHolderId = Holder,
            ReportDate = Quarter,
            Current = [Row(Kept, Quarter, 100, 3_000), Row(Split, Quarter, 100, 1_000)],
            Previous = [Row(Kept, Previous, 100, 3_000)],
            Presence = new Dictionary<HolderSecurityKey, long>
            {
                // Held the two quarters before, and this quarter's bit already stored.
                [new(Kept, null)] = Mask(quarter - 2, quarter - 1, quarter),
                // A position the filer no longer holds still carries this quarter's bit.
                [stale] = Mask(quarter - 1, quarter),
            },
            FiledMask = Mask(quarter - 2, quarter - 1),
        };

        var result = HolderQuarterMetricsBuilder.Build(input, 1);

        result.PresenceToSet.Should().Equal(new HolderSecurityKey(Split, null));
        result.PresenceToClear.Should().Equal(stale);
        // 3,000 held three quarters and 1,000 held one.
        result.Metrics.ValueWeightedTenureQuarters.Should().Be(2.5m);
        result.Metrics.CensoredValueShare.Should().Be(0m);
        result.Metrics.HorizonBand.Should().Be(HolderHorizonBand.Short);
    }

    [Fact]
    public void Build_AQuarterOutsideCoverage_Throws()
    {
        var build = () =>
            HolderQuarterMetricsBuilder.Build(
                new HolderQuarterInput { ReportDate = new DateOnly(2019, 12, 31) },
                1
            );

        build.Should().Throw<ArgumentException>();
    }

    private static HolderIntelligenceRow Row(
        Guid issuer,
        DateOnly reportDate,
        long shares,
        long value,
        ShareType shareType = ShareType.Shares,
        OptionType? optionType = null,
        bool valueMissing = false
    ) =>
        new()
        {
            InstitutionalHolderId = Holder,
            EquityIssuerId = issuer,
            ReportDate = reportDate,
            ShareType = shareType,
            OptionType = optionType,
            Shares = shares,
            Value = value,
            ValueMissing = valueMissing,
        };

    private static long Mask(params int[] quarters) =>
        quarters.Aggregate(0L, HolderTenureCalculator.Set);
}
