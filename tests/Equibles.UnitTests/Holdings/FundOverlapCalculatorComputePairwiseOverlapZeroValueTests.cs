using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Contract: SharedTickerCounts counts stocks with Value &gt; 0. A position that is
/// present but reported at Value 0 must NOT count toward a fund's own diagonal nor
/// toward any pairwise shared count — even though that fund "has" the position. This
/// exercises the `Value &lt;= 0` guard, which the all-positive happy-path test never hits.
/// </summary>
public class FundOverlapCalculatorComputePairwiseOverlapZeroValueTests
{
    private static readonly DateOnly Report = new(2024, 12, 31);

    [Fact]
    public void ComputePairwiseOverlap_FundHoldsStockAtZeroValue_ExcludedFromDiagonalAndSharedCounts()
    {
        // Fund A: AAPL@100, MSFT@200 (two real positions).
        // Fund B: AAPL@0 (present but zero-valued), MSFT@50 (one real position).
        // Per the Value > 0 contract, B's AAPL is not a holding: B's diagonal is 1,
        // and the only shared stock is MSFT. A guardless count would report 2 and 2.
        var aapl = MakeStock("AAPL");
        var msft = MakeStock("MSFT");
        var holderA = MakeHolder("Fund A");
        var holderB = MakeHolder("Fund B");

        var overlap = FundOverlapCalculator.Calculate(
            [
                (holderA, [MakeHolding(holderA, aapl, 100), MakeHolding(holderA, msft, 200)]),
                (holderB, [MakeHolding(holderB, aapl, 0), MakeHolding(holderB, msft, 50)]),
            ],
            Report
        );

        var matrix = FundOverlapCalculator.ComputePairwiseOverlap(overlap);

        matrix.SharedTickerCounts[0][0].Should().Be(2, "Fund A holds AAPL + MSFT, both Value > 0");
        matrix
            .SharedTickerCounts[1][1]
            .Should()
            .Be(1, "Fund B's only Value > 0 position is MSFT; the AAPL@0 row is excluded");
        matrix
            .SharedTickerCounts[0][1]
            .Should()
            .Be(1, "shared = {MSFT} only; AAPL is not shared because B holds it at Value 0");
        matrix.SharedTickerCounts[1][0].Should().Be(1, "symmetry: B∩A = A∩B");
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
