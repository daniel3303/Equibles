using Equibles.Sec.FinancialFacts.BusinessLogic.ReleaseStatements;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.UnitTests.Sec;

// A figure carries its comparative fact's stamp, and facts stamp a discrete fourth quarter as the
// full year and a nine-month span as the third quarter; the span tells them apart.
public class ReleaseFigurePeriodTests
{
    [Theory]
    [InlineData("2026-07-01", "2026-09-30", SecFiscalPeriod.Q3, "Q3")]
    [InlineData("2026-01-01", "2026-09-30", SecFiscalPeriod.Q3, "Q3 YTD")]
    [InlineData("2026-01-01", "2026-06-30", SecFiscalPeriod.Q2, "Q2 YTD")]
    [InlineData("2026-10-01", "2026-12-31", SecFiscalPeriod.FullYear, "Q4")]
    [InlineData("2026-01-01", "2026-12-31", SecFiscalPeriod.FullYear, "FY")]
    [InlineData("2025-12-28", "2027-01-02", SecFiscalPeriod.FullYear, "FY")]
    [InlineData("2026-09-30", "2026-09-30", SecFiscalPeriod.Q3, "Q3")]
    [InlineData("2026-12-31", "2026-12-31", SecFiscalPeriod.FullYear, "FY")]
    public void PeriodOf_NamesThePeriodFromItsSpan(
        string start,
        string end,
        SecFiscalPeriod stamp,
        string expected
    )
    {
        ReleaseFigureReader.PeriodOf(Figure(start, end, stamp)).Label.Should().Be(expected);
    }

    [Theory]
    [InlineData("2026-07-01", "2026-09-30", SecFiscalPeriod.Q3, SecFiscalPeriod.Q3, true)]
    [InlineData("2026-01-01", "2026-09-30", SecFiscalPeriod.Q3, SecFiscalPeriod.Q3, false)]
    [InlineData("2026-10-01", "2026-12-31", SecFiscalPeriod.FullYear, SecFiscalPeriod.Q4, true)]
    [InlineData(
        "2026-10-01",
        "2026-12-31",
        SecFiscalPeriod.FullYear,
        SecFiscalPeriod.FullYear,
        false
    )]
    [InlineData("2026-01-01", "2026-12-31", SecFiscalPeriod.FullYear, SecFiscalPeriod.Q4, false)]
    [InlineData("2026-12-31", "2026-12-31", SecFiscalPeriod.FullYear, SecFiscalPeriod.Q4, true)]
    [InlineData(
        "2026-12-31",
        "2026-12-31",
        SecFiscalPeriod.FullYear,
        SecFiscalPeriod.FullYear,
        true
    )]
    [InlineData("2026-09-30", "2026-09-30", SecFiscalPeriod.Q3, SecFiscalPeriod.FullYear, false)]
    public void Answers_ARequestForOnePeriod_TakesOnlyTheSpanThatIsThatPeriod(
        string start,
        string end,
        SecFiscalPeriod stamp,
        SecFiscalPeriod requested,
        bool expected
    )
    {
        ReleaseFigureReader
            .PeriodOf(Figure(start, end, stamp))
            .Answers(requested)
            .Should()
            .Be(expected);
    }

    private static ReleaseStatementFigure Figure(string start, string end, SecFiscalPeriod stamp) =>
        new()
        {
            PeriodStart = DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture),
            PeriodEnd = DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture),
            FiscalPeriod = stamp,
        };
}
