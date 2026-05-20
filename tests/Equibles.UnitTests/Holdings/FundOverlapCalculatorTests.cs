using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class FundOverlapCalculatorTests
{
    private static readonly DateOnly Report = new(2024, 12, 31);

    [Fact]
    public void Calculate_NoFunds_ReturnsEmptyResult()
    {
        var result = FundOverlapCalculator.Calculate([], Report);

        result.Funds.Should().BeEmpty();
        result.Rows.Should().BeEmpty();
        result.UnionPositionCount.Should().Be(0);
        result.JaccardSimilarityPercent.Should().Be(0);
    }

    [Fact]
    public void Calculate_TwoIdenticalFunds_JaccardIsOneHundredPercent()
    {
        var aapl = MakeStock("AAPL", "Apple Inc.");
        var msft = MakeStock("MSFT", "Microsoft Corp.");
        var fundA = MakeHolder("Fund A", "C001");
        var fundB = MakeHolder("Fund B", "C002");

        var result = FundOverlapCalculator.Calculate(
            [
                (
                    fundA,
                    (IReadOnlyList<InstitutionalHolding>)
                        [
                            MakeHolding(fundA, aapl, shares: 1_000, value: 1_000_000),
                            MakeHolding(fundA, msft, shares: 500, value: 500_000),
                        ]
                ),
                (
                    fundB,
                    (IReadOnlyList<InstitutionalHolding>)
                        [
                            MakeHolding(fundB, aapl, shares: 800, value: 800_000),
                            MakeHolding(fundB, msft, shares: 200, value: 200_000),
                        ]
                ),
            ],
            Report
        );

        result.UnionPositionCount.Should().Be(2);
        result.IntersectionPositionCount.Should().Be(2);
        result.JaccardSimilarityPercent.Should().Be(100.0);
        result.Rows.Should().AllSatisfy(r => r.IsCommon.Should().BeTrue());
    }

    [Fact]
    public void Calculate_TwoDisjointFunds_JaccardIsZero()
    {
        var aapl = MakeStock("AAPL", "Apple Inc.");
        var msft = MakeStock("MSFT", "Microsoft Corp.");
        var fundA = MakeHolder("Fund A", "C001");
        var fundB = MakeHolder("Fund B", "C002");

        var result = FundOverlapCalculator.Calculate(
            [
                (
                    fundA,
                    (IReadOnlyList<InstitutionalHolding>)
                        [MakeHolding(fundA, aapl, shares: 1_000, value: 1_000_000)]
                ),
                (
                    fundB,
                    (IReadOnlyList<InstitutionalHolding>)
                        [MakeHolding(fundB, msft, shares: 500, value: 500_000)]
                ),
            ],
            Report
        );

        result.UnionPositionCount.Should().Be(2);
        result.IntersectionPositionCount.Should().Be(0);
        result.JaccardSimilarityPercent.Should().Be(0);
        result.DollarWeightedOverlapPercent.Should().Be(0);
    }

    [Fact]
    public void Calculate_PartialOverlap_ProducesCorrectJaccard()
    {
        var aapl = MakeStock("AAPL", "Apple Inc.");
        var msft = MakeStock("MSFT", "Microsoft Corp.");
        var nvda = MakeStock("NVDA", "NVIDIA Corp.");
        var fundA = MakeHolder("Fund A", "C001");
        var fundB = MakeHolder("Fund B", "C002");

        var result = FundOverlapCalculator.Calculate(
            [
                (
                    fundA,
                    (IReadOnlyList<InstitutionalHolding>)
                        [
                            MakeHolding(fundA, aapl, shares: 1_000, value: 1_000_000),
                            MakeHolding(fundA, msft, shares: 500, value: 500_000),
                        ]
                ),
                (
                    fundB,
                    (IReadOnlyList<InstitutionalHolding>)
                        [
                            MakeHolding(fundB, aapl, shares: 800, value: 800_000),
                            MakeHolding(fundB, nvda, shares: 300, value: 300_000),
                        ]
                ),
            ],
            Report
        );

        // Union = {AAPL, MSFT, NVDA} → 3; Intersection = {AAPL} → 1; Jaccard = 1/3 ≈ 33.33%.
        result.UnionPositionCount.Should().Be(3);
        result.IntersectionPositionCount.Should().Be(1);
        result.JaccardSimilarityPercent.Should().BeApproximately(33.33, precision: 0.01);
    }

    [Fact]
    public void Calculate_RowsOrderedByCombinedValueDesc()
    {
        var aapl = MakeStock("AAPL", "Apple Inc.");
        var msft = MakeStock("MSFT", "Microsoft Corp.");
        var fundA = MakeHolder("Fund A", "C001");

        var result = FundOverlapCalculator.Calculate(
            [
                (
                    fundA,
                    (IReadOnlyList<InstitutionalHolding>)
                        [
                            MakeHolding(fundA, msft, shares: 500, value: 500_000),
                            MakeHolding(fundA, aapl, shares: 1_000, value: 1_000_000),
                        ]
                ),
            ],
            Report
        );

        result.Rows.Should().HaveCount(2);
        result.Rows[0].Ticker.Should().Be("AAPL");
        result.Rows[1].Ticker.Should().Be("MSFT");
    }

    [Fact]
    public void Calculate_PercentOfPortfolio_DividesByFundTotalValue()
    {
        var aapl = MakeStock("AAPL", "Apple Inc.");
        var msft = MakeStock("MSFT", "Microsoft Corp.");
        var fundA = MakeHolder("Fund A", "C001");

        var result = FundOverlapCalculator.Calculate(
            [
                (
                    fundA,
                    (IReadOnlyList<InstitutionalHolding>)
                        [
                            MakeHolding(fundA, aapl, shares: 1_000, value: 700_000),
                            MakeHolding(fundA, msft, shares: 500, value: 300_000),
                        ]
                ),
            ],
            Report
        );

        var aaplRow = result.Rows.Single(r => r.Ticker == "AAPL");
        var msftRow = result.Rows.Single(r => r.Ticker == "MSFT");
        aaplRow.Slices[0].PercentOfPortfolio.Should().Be(70.0);
        msftRow.Slices[0].PercentOfPortfolio.Should().Be(30.0);
    }

    private static CommonStock MakeStock(string ticker, string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = name,
            Cik = "C" + Guid.NewGuid().ToString("N")[..7],
        };

    private static InstitutionalHolder MakeHolder(string name, string cik) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Cik = cik,
        };

    private static InstitutionalHolding MakeHolding(
        InstitutionalHolder holder,
        CommonStock stock,
        long shares,
        long value
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            CommonStock = stock,
            InstitutionalHolderId = holder.Id,
            InstitutionalHolder = holder,
            FilingDate = Report.AddDays(45),
            ReportDate = Report,
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
        };
}
