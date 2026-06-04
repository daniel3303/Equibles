using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class InstitutionPortfolioSummaryCalculatorTurnoverNoPriorQuarterTests
{
    // Quarter-over-quarter turnover is undefined without a prior quarter to
    // compare against — a first-ever 13F filer has no previous holdings. The
    // `previousQuarterHoldings.Count > 0` guard must leave QoQTurnoverPercent at
    // its default (0). A regression dropping the guard would feed an empty prior
    // quarter into the calc, read every current position as newly initiated, and
    // report a spurious ~50% turnover for a debut filing.
    [Fact]
    public void Calculate_NoPreviousQuarterHoldings_LeavesTurnoverAtZero()
    {
        var stockA = Guid.NewGuid();
        var stockB = Guid.NewGuid();
        var currentA = MakeHolding(stockA, shares: 1_000, value: 1_000_000);
        var currentB = MakeHolding(stockB, shares: 500, value: 500_000);

        var result = InstitutionPortfolioSummaryCalculator.Calculate(
            [currentA, currentB],
            [],
            quartersReported: 1,
            latestReportDate: new DateOnly(2024, 12, 31),
            previousReportDate: null
        );

        result.QoQTurnoverPercent.Should().Be(0.0);
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
