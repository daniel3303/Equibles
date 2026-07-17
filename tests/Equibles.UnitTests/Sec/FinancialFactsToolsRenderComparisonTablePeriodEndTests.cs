using System.Reflection;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// XBRL fiscal years are filer-relative: a "fiscal 2025 Q2" comparison mixed
/// NVDA's quarter ended 2024-07-28 with AMD/INTC quarters ended mid-2025 —
/// ~11 months apart — with nothing but the Filed column hinting at it. The
/// comparison table must carry each row's Period End, and flag the fiscal-
/// calendar misalignment when peer period ends spread wider than a quarter.
/// </summary>
public class FinancialFactsToolsRenderComparisonTablePeriodEndTests
{
    private static FinancialFact QuarterFact(DateOnly periodEnd, decimal value) =>
        new()
        {
            Value = value,
            Unit = "USD/shares",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = periodEnd.AddDays(-91),
            PeriodEnd = periodEnd,
            FiscalYear = 2025,
            FiscalPeriod = SecFiscalPeriod.Q2,
            Form = DocumentType.TenQ,
            FiledDate = periodEnd.AddDays(30),
            AccessionNumber = "acc",
        };

    private static string Render(params (string Ticker, DateOnly PeriodEnd)[] rows)
    {
        var method = typeof(FinancialFactsTools).GetMethod(
            "RenderComparisonTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var tableRows = rows.Select(r =>
                (r.Ticker, $"{r.Ticker} Inc.", QuarterFact(r.PeriodEnd, 1m))
            )
            .ToList();
        return (string)
            method.Invoke(
                null,
                ["eps-diluted", 2025, SecFiscalPeriod.Q2, tableRows, new List<string>()]
            );
    }

    [Fact]
    public void RenderComparisonTable_Always_CarriesPeriodEndColumn()
    {
        var result = Render(("NVDA", new DateOnly(2024, 7, 28)));

        result.Should().Contain("| Period End |");
        result.Should().Contain("| 2024-07-28 |");
    }

    [Fact]
    public void RenderComparisonTable_PeriodEndsSpreadBeyondAQuarter_FlagsFiscalCalendarMismatch()
    {
        var result = Render(
            ("NVDA", new DateOnly(2024, 7, 28)),
            ("AMD", new DateOnly(2025, 6, 28))
        );

        result.Should().Contain("fiscal calendars differ");
        result.Should().Contain("2024-07-28 to 2025-06-28");
    }

    [Fact]
    public void RenderComparisonTable_AlignedPeriodEnds_NoMismatchNote()
    {
        var result = Render(
            ("AAPL", new DateOnly(2023, 9, 30)),
            ("MSFT", new DateOnly(2023, 6, 30))
        );

        result.Should().NotContain("fiscal calendars differ");
    }
}
