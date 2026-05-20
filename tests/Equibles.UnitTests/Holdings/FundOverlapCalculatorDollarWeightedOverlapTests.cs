using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class FundOverlapCalculatorDollarWeightedOverlapTests
{
    private static readonly DateOnly Report = new(2024, 12, 31);

    [Fact]
    public void Calculate_PartialOverlap_DollarWeightedIsSumMinOverSumMax()
    {
        // Existing PartialOverlap test pins Jaccard but not DollarWeighted. The
        // weighted-Jaccard formula is sum(min(value_per_fund) for shared stocks)
        // over sum(max(value_per_fund) across all union stocks). For:
        //   AAPL: A=$1,000,000, B=$800,000   (shared) → min 800,000, max 1,000,000
        //   MSFT: A=$500,000              (A only)  → max 500,000
        //   NVDA: B=$300,000              (B only)  → max 300,000
        // numerator = 800,000; denominator = 1,000,000 + 500,000 + 300,000 = 1,800,000.
        // Expected: 800,000 / 1,800,000 * 100 ≈ 44.4444…%.
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

        result.DollarWeightedOverlapPercent.Should().BeApproximately(44.44, precision: 0.01);
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
