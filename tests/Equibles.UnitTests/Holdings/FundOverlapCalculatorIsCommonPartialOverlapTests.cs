using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class FundOverlapCalculatorIsCommonPartialOverlapTests
{
    private static readonly DateOnly Report = new(2024, 12, 31);

    // The existing TwoIdenticalFunds pin asserts every row's IsCommon = true
    // when both funds hold the same set of stocks. The partial-overlap arm —
    // a stock held by ONE fund must carry IsCommon = false while the SHARED
    // stock keeps IsCommon = true — is unpinned. A refactor that hard-coded
    // IsCommon = true (or tied it to a different signal like
    // `CombinedValue > 0`) would pass the all-identical pin yet mislabel
    // every single-fund row as "consensus", breaking the consensus-only
    // filters in the consumer views and the JaccardSimilarity ratio that
    // derives IntersectionPositionCount from `allHaveIt`.
    [Fact]
    public void Calculate_PartialOverlap_IsCommonTrueOnSharedRowAndFalseOnSingleFundRows()
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

        var aaplRow = result.Rows.Single(r => r.Ticker == "AAPL");
        var msftRow = result.Rows.Single(r => r.Ticker == "MSFT");
        var nvdaRow = result.Rows.Single(r => r.Ticker == "NVDA");

        aaplRow.IsCommon.Should().BeTrue();
        msftRow.IsCommon.Should().BeFalse();
        nvdaRow.IsCommon.Should().BeFalse();
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
