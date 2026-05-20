using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories.Projections;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins the InstitutionPortfolioSummaryCalculator contract — the pure function
/// that produces the AUM / position-count / concentration / turnover stat strip
/// shown on the institution profile page (issue #1011) and the equivalent MCP
/// summary tool. A regression here would silently misreport fund metrics on
/// both surfaces, so each metric and each documented edge case is fixed in a
/// separate test.
/// </summary>
public class InstitutionPortfolioSummaryCalculatorTests
{
    private static readonly DateOnly Q4_2024 = new(2024, 12, 31);
    private static readonly DateOnly Q3_2024 = new(2024, 9, 30);

    [Fact]
    public void Calculate_EmptyCurrentQuarter_ReturnsZeroedSummaryWithMetadataPreserved()
    {
        // Edge case: a holder row exists but no holdings are recorded. The
        // calculator must not divide by zero AUM and must still expose the
        // metadata fields the view binds to (quartersReported + the report-date
        // chips).
        var summary = InstitutionPortfolioSummaryCalculator.Calculate(
            [],
            [],
            quartersReported: 0,
            latestReportDate: null,
            previousReportDate: null
        );

        summary.ReportedAum.Should().Be(0);
        summary.PositionCount.Should().Be(0);
        summary.Top10ConcentrationPercent.Should().Be(0m);
        summary.Top25ConcentrationPercent.Should().Be(0m);
        summary.QuarterOverQuarterTurnoverPercent.Should().BeNull();
        summary.QuartersReported.Should().Be(0);
        summary.LatestReportDate.Should().BeNull();
        summary.PreviousReportDate.Should().BeNull();
    }

    [Fact]
    public void Calculate_SinglePosition_ReportsAumPositionCountAndOneHundredPercentConcentration()
    {
        // A single-stock portfolio is the smallest non-empty case. AUM equals
        // the row value, position count equals 1, and top-10 / top-25
        // concentration must round to exactly 100% — anything else means the
        // calculator double-counted or under-summed the lone position.
        var stockId = Guid.NewGuid();
        var summary = InstitutionPortfolioSummaryCalculator.Calculate(
            [Holding(stockId, shares: 1_000, value: 5_000_000)],
            [],
            quartersReported: 1,
            latestReportDate: Q4_2024,
            previousReportDate: null
        );

        summary.ReportedAum.Should().Be(5_000_000);
        summary.PositionCount.Should().Be(1);
        summary.Top10ConcentrationPercent.Should().Be(100m);
        summary.Top25ConcentrationPercent.Should().Be(100m);
        summary.QuarterOverQuarterTurnoverPercent.Should().BeNull();
    }

    [Fact]
    public void Calculate_TopTenConcentration_IsValueOfTopTenStocksDividedByAum()
    {
        // Build 15 distinct positions with descending values so the top-10
        // concentration ratio is exact. This pins the ranking + slice + ratio
        // logic in one shot: a regression that took the bottom 10 or rounded
        // the ratio prematurely would fail this assertion.
        var holdings = new List<InstitutionalHolding>();
        for (var i = 1; i <= 15; i++)
        {
            holdings.Add(Holding(Guid.NewGuid(), shares: 100, value: i * 1_000_000L));
        }

        var summary = InstitutionPortfolioSummaryCalculator.Calculate(
            holdings,
            [],
            quartersReported: 1,
            latestReportDate: Q4_2024,
            previousReportDate: null
        );

        // sum 1..15 = 120; top 10 (6..15) = 105; 105/120 = 87.5%
        summary.ReportedAum.Should().Be(120_000_000);
        summary.PositionCount.Should().Be(15);
        summary.Top10ConcentrationPercent.Should().BeApproximately(87.5m, 0.01m);
        // top 25 with only 15 positions == 100%
        summary.Top25ConcentrationPercent.Should().Be(100m);
    }

    [Fact]
    public void Calculate_MultipleRowsForSameStock_AggregatesAcrossRowsForPositionCount()
    {
        // 13F filings can carry multiple rows for the same stock (different
        // share classes, options legs, manager attribution). The position
        // count is documented as COUNT(DISTINCT CommonStockId), so two rows
        // for the same stock collapse to one position and combine into AUM.
        var stockId = Guid.NewGuid();
        var summary = InstitutionPortfolioSummaryCalculator.Calculate(
            [
                Holding(stockId, shares: 100, value: 1_000_000),
                Holding(stockId, shares: 50, value: 500_000),
            ],
            [],
            quartersReported: 1,
            latestReportDate: Q4_2024,
            previousReportDate: null
        );

        summary.PositionCount.Should().Be(1);
        summary.ReportedAum.Should().Be(1_500_000);
    }

    [Fact]
    public void Calculate_NoPriorQuarter_TurnoverIsNull()
    {
        // Documented contract: the QoQ turnover field is "—" on the UI when
        // there's no prior quarter to compare. The calculator must return null
        // (not zero) so the view can distinguish "0% turnover this quarter"
        // from "no prior quarter to compute against".
        var summary = InstitutionPortfolioSummaryCalculator.Calculate(
            [Holding(Guid.NewGuid(), shares: 100, value: 1_000_000)],
            [],
            quartersReported: 1,
            latestReportDate: Q4_2024,
            previousReportDate: null
        );

        summary.QuarterOverQuarterTurnoverPercent.Should().BeNull();
    }

    [Fact]
    public void Calculate_PortfolioUnchanged_TurnoverIsZero()
    {
        // Same positions and share counts in both quarters → no dollar change,
        // so turnover must be exactly zero (not null — there IS a prior
        // quarter). A regression that priced the unchanged shares against
        // themselves would still return zero, but any sign error or absolute-
        // value bug would surface here.
        var stockA = Guid.NewGuid();
        var stockB = Guid.NewGuid();
        var holdings = new[]
        {
            Holding(stockA, shares: 100, value: 1_000_000),
            Holding(stockB, shares: 200, value: 2_000_000),
        };

        var summary = InstitutionPortfolioSummaryCalculator.Calculate(
            holdings,
            holdings,
            quartersReported: 2,
            latestReportDate: Q4_2024,
            previousReportDate: Q3_2024
        );

        summary.QuarterOverQuarterTurnoverPercent.Should().Be(0m);
    }

    [Fact]
    public void Calculate_FullPortfolioRotation_TurnoverIsOneHundredPercent()
    {
        // The two-sided turnover formula (Σ|Δshares × price| / (2 × AUM)) caps
        // at 100% when every dollar is reallocated to entirely new positions.
        // Stock A is fully exited; Stock B is freshly initiated with the same
        // dollar value. Total dollar movement = 2 × AUM → turnover = 100%.
        var stockA = Guid.NewGuid();
        var stockB = Guid.NewGuid();

        var summary = InstitutionPortfolioSummaryCalculator.Calculate(
            [Holding(stockB, shares: 200, value: 2_000_000)],
            [Holding(stockA, shares: 100, value: 2_000_000)],
            quartersReported: 2,
            latestReportDate: Q4_2024,
            previousReportDate: Q3_2024
        );

        summary.QuarterOverQuarterTurnoverPercent.Should().BeApproximately(100m, 0.01m);
    }

    private static InstitutionalHolding Holding(Guid stockId, long shares, long value) =>
        new()
        {
            CommonStockId = stockId,
            Shares = shares,
            Value = value,
            ReportDate = Q4_2024,
        };
}
