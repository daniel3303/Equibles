using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialStatementToolsPickCurrentlyReportedFactInstantTests
{
    // A 10-Q balance sheet carries the current period-end instant alongside a prior
    // comparative instant (the prior fiscal-year-end), both re-tagged under the same
    // fiscal (year, period) and filed on the same day. The currently-reported figure is
    // the instant ending latest; selecting by filed date alone is a wash on the shared
    // date, so a comparative column could stand in for the current one — which is exactly
    // why the balance sheet stopped balancing (#1546: AAPL FY2025 Q2 equity showed the
    // $74.1B 2023-12-30 comparative instead of the real $66.8B at 2025-03-29). The
    // comparative is listed first to pin that the pick is the latest period-end, not
    // input order.
    [Fact]
    public void PickCurrentlyReportedFact_BalanceSheetWithComparativeInstant_PicksLatestPeriodEnd()
    {
        var conceptId = Guid.NewGuid();
        var comparative = MakeInstant(
            conceptId,
            value: 74_100m,
            periodEnd: new DateOnly(2023, 12, 30)
        );
        var current = MakeInstant(conceptId, value: 66_796m, periodEnd: new DateOnly(2025, 3, 29));

        var result = FinancialStatementTools.PickCurrentlyReportedFact(
            [comparative, current],
            SecFiscalPeriod.Q2
        );

        result
            .Value.Should()
            .Be(66_796m, "the current period-end instant outranks the comparative column");
    }

    private static FinancialFact MakeInstant(Guid conceptId, decimal value, DateOnly periodEnd) =>
        new()
        {
            CommonStockId = Guid.NewGuid(),
            FinancialConceptId = conceptId,
            Value = value,
            Unit = "USD",
            PeriodStart = periodEnd,
            PeriodEnd = periodEnd,
            FiscalYear = 2025,
            FiscalPeriod = SecFiscalPeriod.Q2,
            PeriodType = FactPeriodType.Instant,
            Form = DocumentType.TenQ,
            FiledDate = new DateOnly(2025, 5, 2),
            AccessionNumber = "0000320193-25-000057",
        };
}
