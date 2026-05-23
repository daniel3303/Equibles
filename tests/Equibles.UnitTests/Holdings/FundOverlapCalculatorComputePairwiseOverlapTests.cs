using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Contract: ComputePairwiseOverlap builds an n×n symmetric matrix where
/// SharedTickerCounts[i][j] = stocks with Value > 0 shared by fund i and j.
/// Diagonal [i][i] = stocks with Value > 0 held by fund i.
/// </summary>
public class FundOverlapCalculatorComputePairwiseOverlapTests
{
    private static readonly DateOnly Report = new(2024, 12, 31);

    [Fact]
    public void ComputePairwiseOverlap_ThreeFundsPartialOverlap_MatrixIsSymmetricAndDiagonalCountsOwnPositions()
    {
        // Fund A: AAPL, MSFT (2 positions)
        // Fund B: AAPL, GOOG (2 positions)
        // Fund C: MSFT, GOOG, TSLA (3 positions)
        // A∩B = {AAPL} = 1, A∩C = {MSFT} = 1, B∩C = {GOOG} = 1
        var aapl = MakeStock("AAPL");
        var msft = MakeStock("MSFT");
        var goog = MakeStock("GOOG");
        var tsla = MakeStock("TSLA");
        var holderA = MakeHolder("Fund A");
        var holderB = MakeHolder("Fund B");
        var holderC = MakeHolder("Fund C");

        var overlap = FundOverlapCalculator.Calculate(
            [
                (holderA, [MakeHolding(holderA, aapl, 100), MakeHolding(holderA, msft, 200)]),
                (holderB, [MakeHolding(holderB, aapl, 50), MakeHolding(holderB, goog, 300)]),
                (
                    holderC,
                    [
                        MakeHolding(holderC, msft, 150),
                        MakeHolding(holderC, goog, 250),
                        MakeHolding(holderC, tsla, 400),
                    ]
                ),
            ],
            Report
        );

        var matrix = FundOverlapCalculator.ComputePairwiseOverlap(overlap);

        matrix.SharedTickerCounts.Should().HaveCount(3);

        // Diagonal: each fund's own position count (Value > 0)
        matrix.SharedTickerCounts[0][0].Should().Be(2, "Fund A holds AAPL + MSFT");
        matrix.SharedTickerCounts[1][1].Should().Be(2, "Fund B holds AAPL + GOOG");
        matrix.SharedTickerCounts[2][2].Should().Be(3, "Fund C holds MSFT + GOOG + TSLA");

        // Off-diagonal: shared count, symmetric
        matrix.SharedTickerCounts[0][1].Should().Be(1, "A∩B = {AAPL}");
        matrix.SharedTickerCounts[1][0].Should().Be(1, "symmetry: B∩A = A∩B");
        matrix.SharedTickerCounts[0][2].Should().Be(1, "A∩C = {MSFT}");
        matrix.SharedTickerCounts[2][0].Should().Be(1, "symmetry: C∩A = A∩C");
        matrix.SharedTickerCounts[1][2].Should().Be(1, "B∩C = {GOOG}");
        matrix.SharedTickerCounts[2][1].Should().Be(1, "symmetry: C∩B = B∩C");
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
