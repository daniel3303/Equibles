using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class HistoricalCalendarImportTests
{
    [Theory]
    [InlineData("Q1", SecFiscalPeriod.Q1)]
    [InlineData("FY", SecFiscalPeriod.FullYear)]
    public void ShortReportedPeriodRetainsSourceIdentity(
        string sourcePeriod,
        SecFiscalPeriod expected
    )
    {
        var value = new CompanyFactValue
        {
            Start = new DateOnly(2025, 2, 1),
            End = new DateOnly(2025, 3, 31),
            Fy = 2025,
            Fp = sourcePeriod,
            Val = 123m,
            Form = "10-Q",
            Accn = "0001274173-25-000001",
            Filed = new DateOnly(2025, 5, 1),
        };
        var calendar = new HistoricalFiscalCalendar(
            [(new(2025, 1, 1), new(2025, 12, 31))],
            [],
            6,
            30,
            true
        );
        var method = typeof(FinancialFactsImportService).GetMethod(
            "TryBuildParsedFactWithCalendar",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        var parsed = method.Invoke(
            null,
            [
                FactTaxonomy.UsGaap,
                "Revenues",
                "Revenue",
                "",
                "USD",
                value,
                Equibles.TestSupport.EquityIssuerSeed.Create(
                    FiscalYearEndMonth: 6,
                    FiscalYearEndDay: 30
                ),
                calendar,
            ]
        );
        parsed.Should().NotBeNull();
        parsed!.GetType().GetProperty("FiscalYear")!.GetValue(parsed).Should().Be(2025);
        parsed.GetType().GetProperty("FiscalPeriod")!.GetValue(parsed).Should().Be(expected);
    }
}
