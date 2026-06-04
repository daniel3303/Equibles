using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Contract: ComputePairwiseOverlap builds an n×n matrix; the diagonal [i][i] is fund
/// i's own count of stocks with Value &gt; 0. The n=1 boundary is the smallest valid
/// matrix — the nested `for j = i` loop degenerates so the `i != j` mirror branch never
/// runs. The multi-fund happy-path and zero-value tests never exercise n=1, where an
/// off-by-one in the matrix sizing (e.g. `new int[n-1][]` or `j &lt;= n`) would surface.
/// </summary>
public class FundOverlapCalculatorComputePairwiseOverlapSingleFundTests
{
    private static readonly DateOnly Report = new(2024, 12, 31);

    [Fact]
    public void ComputePairwiseOverlap_SingleFund_ReturnsOneByOneMatrixWithDiagonalEqualToPositionCount()
    {
        // One fund holding two real positions. The oracle, derived purely from the
        // contract: a 1×1 matrix whose sole cell counts the fund's Value > 0 stocks (2).
        var aapl = MakeStock("AAPL");
        var msft = MakeStock("MSFT");
        var holderA = MakeHolder("Fund A");

        var overlap = FundOverlapCalculator.Calculate(
            [(holderA, [MakeHolding(holderA, aapl, 100), MakeHolding(holderA, msft, 200)])],
            Report
        );

        var matrix = FundOverlapCalculator.ComputePairwiseOverlap(overlap);

        matrix.SharedTickerCounts.Should().HaveCount(1, "a single fund yields a 1×1 matrix");
        matrix.SharedTickerCounts[0].Should().HaveCount(1, "the only row has a single column");
        matrix
            .SharedTickerCounts[0][0]
            .Should()
            .Be(2, "the lone diagonal cell counts the fund's two Value > 0 positions");
    }

    private static CommonStock MakeStock(string ticker) =>
        new()
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = ticker,
        };

    private static InstitutionalHolder MakeHolder(string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Cik = "C" + name.GetHashCode(),
        };

    private static InstitutionalHolding MakeHolding(
        InstitutionalHolder holder,
        CommonStock stock,
        long value
    ) =>
        new()
        {
            InstitutionalHolderId = holder.Id,
            CommonStockId = stock.Id,
            CommonStock = stock,
            Shares = value,
            Value = value,
        };
}
