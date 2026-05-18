using Equibles.CommonStocks.BusinessLogic;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Adversarial sibling to <see cref="FiscalCalendarTests"/>, which only pins
/// September/June/December filers. Contract (FiscalCalendar + FiscalPeriod
/// docs): the period is derived from the fiscal-year-end month, and the year
/// is labelled by the calendar year the fiscal year *ends* in. The entire
/// retail sector (Walmart, Target, Home Depot) ends its fiscal year in
/// January, so FY2024 runs Feb 2023–Jan 2024 with Q4 = Nov 2023–Jan 2024.
/// A December-2023 date must therefore map to FY2024 Q4 — exercising both the
/// year-label flip and the early-end-month quarter wrap, untested until now.
/// </summary>
public class FiscalCalendarJanuaryFiscalYearEndTests
{
    [Fact]
    public void GetPeriod_JanuaryFiscalYearEnd_DecemberDate_IsNextYearQ4()
    {
        var period = FiscalCalendar.GetPeriod(new DateOnly(2023, 12, 31), fiscalYearEndMonth: 1);

        period.Should().Be(new FiscalPeriod(2024, 4));
    }
}
