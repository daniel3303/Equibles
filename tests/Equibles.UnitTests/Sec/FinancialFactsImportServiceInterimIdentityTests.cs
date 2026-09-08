using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceInterimIdentityTests
{
    [Theory]
    [InlineData(1, 31, "2020-02-03", "2020-05-03", 2020, "Q1", 2021, SecFiscalPeriod.Q1)]
    [InlineData(1, 31, "2020-05-04", "2020-08-02", 2020, "Q2", 2021, SecFiscalPeriod.Q2)]
    [InlineData(1, 31, "2020-08-03", "2020-11-01", 2020, "Q3", 2021, SecFiscalPeriod.Q3)]
    [InlineData(12, 31, "2024-01-01", "2024-03-31", 2025, "Q1", 2024, SecFiscalPeriod.Q1)]
    [InlineData(9, 30, "2024-10-01", "2024-12-31", 2025, "FY", 2025, SecFiscalPeriod.Q1)]
    public void MatchingBalanceAndFlowUseTheirMeasuredPeriod(
        int month,
        int day,
        string start,
        string end,
        int wireYear,
        string wirePeriod,
        int expectedYear,
        SecFiscalPeriod expectedPeriod
    )
    {
        var stock = new CommonStock { FiscalYearEndMonth = month, FiscalYearEndDay = day };
        var instant = Parse(stock, null, DateOnly.Parse(end), wireYear, wirePeriod);
        var duration = Parse(
            stock,
            DateOnly.Parse(start),
            DateOnly.Parse(end),
            wireYear,
            wirePeriod
        );

        instant.Should().Be((expectedYear, expectedPeriod));
        duration.Should().Be(instant);
    }

    private static (int Year, SecFiscalPeriod Period) Parse(
        CommonStock stock,
        DateOnly? start,
        DateOnly end,
        int year,
        string period
    )
    {
        var value = new CompanyFactValue
        {
            Start = start,
            End = end,
            Fy = year,
            Fp = period,
            Val = 100,
            Accn = "0000354950-20-000020",
            Form = "10-Q",
            Filed = end.AddDays(30),
        };
        var method = typeof(FinancialFactsImportService).GetMethod(
            "TryBuildParsedFact",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;
        var parsed = method.Invoke(
            null,
            [FactTaxonomy.UsGaap, "Assets", "Assets", null, "USD", value, stock]
        )!;
        return (
            (int)parsed.GetType().GetProperty("FiscalYear")!.GetValue(parsed)!,
            (SecFiscalPeriod)parsed.GetType().GetProperty("FiscalPeriod")!.GetValue(parsed)!
        );
    }
}
