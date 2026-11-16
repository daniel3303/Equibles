using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Contract: SharedTickerCounts counts the stocks each fund holds. A row reporting zero shares
/// and $0 (a position left during the quarter) is no holding, so it counts toward neither the
/// fund's own diagonal nor any pairwise shared count, which the all-positive happy-path test
/// never exercises.
/// </summary>
public class FundOverlapCalculatorComputePairwiseOverlapZeroValueTests
{
    private static readonly DateOnly Report = new(2024, 12, 31);

    [Fact]
    public void ComputePairwiseOverlap_FundRowWithNoSharesAndNoValue_ExcludedFromDiagonalAndSharedCounts()
    {
        // Fund A: AAPL@100, MSFT@200 (two real positions).
        // Fund B: AAPL with zero shares and $0 (left during the quarter), MSFT@50.
        // B's AAPL is not a holding: B's diagonal is 1, and the only shared stock is MSFT.
        // A guardless count would report 2 and 2.
        EquityIssuer aapl = MakeStock("AAPL");
        EquityIssuer msft = MakeStock("MSFT");
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

        matrix.SharedTickerCounts[0][0].Should().Be(2, "Fund A holds AAPL + MSFT");
        matrix
            .SharedTickerCounts[1][1]
            .Should()
            .Be(1, "Fund B holds only MSFT; the zero-share $0 AAPL row is excluded");
        matrix
            .SharedTickerCounts[0][1]
            .Should()
            .Be(1, "shared = {MSFT} only; B does not hold AAPL");
        matrix.SharedTickerCounts[1][0].Should().Be(1, "symmetry: B∩A = A∩B");
    }

    private static EquityIssuer MakeStock(string ticker) =>
        Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: ticker,
            Name: ticker
        );

    private static InstitutionalHolder MakeHolder(string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Cik = "C" + name.GetHashCode(),
        };

    private static InstitutionalHolding MakeHolding(
        InstitutionalHolder holder,
        EquityIssuer stock,
        long value
    ) =>
        new()
        {
            InstitutionalHolderId = holder.Id,
            EquityIssuerId = stock.Id,
            Issuer = Equibles.TestSupport.NativeListingSeed.ForStock(null, stock).Security.Issuer,
            Shares = value,
            Value = value,
        };
}
