using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class InstitutionPortfolioSummaryCalculatorTurnoverNewlyInitiatedPositionTests
{
    // ComputeQoQTurnoverPercent's inline contract: "For each stock that appears
    // in either quarter, |Δ shares × current price proxy| is the absolute dollar
    // movement; the canonical turnover formula then divides by 2 × AUM." A
    // newly-initiated position (priorShares = 0, currentShares > 0) has a
    // current-quarter perShare proxy = Value / Shares, so its contribution is
    // currentShares × perShare = current.Value — i.e. the full position dollar
    // value flows into turnover. The Increased / SoldOut / DecreasedPosition
    // sibling pins cover the three other arms (incremental buy, full exit,
    // partial trim). The newly-initiated arm has no dedicated pin, so a refactor
    // that mirrors the sold-out fallback ("no previous shares → no proxy → 0")
    // would silently drop the contribution and report 0% turnover even for a
    // 100%-rotated portfolio.
    [Fact]
    public void Calculate_TurnoverWithNewlyInitiatedPosition_CountsFullPositionDollarValue()
    {
        // Stock A: unchanged at $1_000_000 — contributes 0 to turnover.
        // Stock B: newly initiated at 500 shares × $500_000 value — perShare proxy
        // is $1_000, |Δ shares| = 500, contribution = 500 × $1_000 = $500_000.
        var stockA = Guid.NewGuid();
        var stockB = Guid.NewGuid();
        var currentA = MakeHolding(stockA, shares: 1_000, value: 1_000_000);
        var currentBNew = MakeHolding(stockB, shares: 500, value: 500_000);
        var priorA = MakeHolding(stockA, shares: 1_000, value: 1_000_000);

        var result = InstitutionPortfolioSummaryCalculator.Calculate(
            [currentA, currentBNew],
            [priorA],
            quartersReported: 2,
            latestReportDate: new DateOnly(2024, 12, 31),
            previousReportDate: new DateOnly(2024, 9, 30)
        );

        // AUM = $1_000_000 + $500_000 = $1_500_000.
        // turnover% = $500_000 / (2 × $1_500_000) × 100 ≈ 16.6667%.
        result.QoQTurnoverPercent.Should().BeApproximately(16.6667, precision: 0.01);
    }

    private static InstitutionalHolding MakeHolding(Guid stockId, long shares, long value) =>
        new()
        {
            CommonStockId = stockId,
            InstitutionalHolderId = Guid.NewGuid(),
            FilingDate = new DateOnly(2025, 1, 15),
            ReportDate = new DateOnly(2024, 12, 31),
            Shares = shares,
            Value = value,
        };
}
