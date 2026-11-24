using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

public class DerivedFourthQuarterTests
{
    private readonly Guid _stockId = Guid.NewGuid();
    private readonly Guid _conceptId = Guid.NewGuid();

    // A year of 120 with first three quarters of 30, 25 and 30 shows a fourth of 35, dated from the
    // day after the third quarter to the year end and filed with the latest of its inputs.
    [Fact]
    public void Append_YearAndItsFirstThreeQuarters_AddsTheFourthAsTheRemainder()
    {
        var rows = Calendar(120m);

        var fourth = DerivedFourthQuarter.Append(rows, rejectNegative: false).Except(rows).Single();

        fourth.Value.Should().Be(35m);
        fourth.FiscalPeriod.Should().Be(SecFiscalPeriod.Q4);
        fourth.FiscalYear.Should().Be(2025);
        fourth.PeriodStart.Should().Be(new DateOnly(2025, 10, 1));
        fourth.PeriodEnd.Should().Be(new DateOnly(2025, 12, 31));
        fourth.FiledDate.Should().Be(new DateOnly(2026, 2, 20));
        fourth.Form.Should().BeNull();
    }

    // After a tag switch the year shows under the new tag and its quarters only under the old one,
    // which states the same year; another tag's quarters that state no year add nothing.
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Append_QuartersUnderAnotherTag_AddTheFourthOnlyWhenTheirTagStatesTheYear(
        bool oldTagStatesTheYear,
        int added
    )
    {
        var rows = Calendar(120m);
        var oldTag = Guid.NewGuid();
        foreach (var quarter in rows.Where(f => f.FiscalPeriod != SecFiscalPeriod.FullYear))
            quarter.FinancialConceptId = oldTag;
        var stated = rows.ToList();
        if (oldTagStatesTheYear)
        {
            var oldYear = Fact("2025-01-01", "2025-12-31", 120m, SecFiscalPeriod.FullYear, "USD");
            oldYear.FinancialConceptId = oldTag;
            stated.Add(oldYear);
        }

        DerivedFourthQuarter
            .Append(rows, rejectNegative: false, stated)
            .Should()
            .HaveCount(rows.Count + added);
    }

    [Theory]
    [InlineData("reported-fourth")]
    [InlineData("statement-table-quarters")]
    [InlineData("reported-fourth-other-tag")]
    [InlineData("missing-second")]
    [InlineData("other-unit")]
    [InlineData("other-concept")]
    [InlineData("negative")]
    public void Append_WithoutExactlyTheThreeQuartersOrBesideAReportedFourth_AddsNothing(
        string shape
    )
    {
        var rows = Calendar(shape == "negative" ? 50m : 120m);
        switch (shape)
        {
            case "reported-fourth":
                rows.Add(Fact("2025-10-01", "2025-12-31", 35m, SecFiscalPeriod.Q4, "USD"));
                break;
            case "reported-fourth-other-tag":
                var fourth = Fact("2025-10-01", "2025-12-31", 35m, SecFiscalPeriod.Q4, "USD");
                fourth.FinancialConceptId = Guid.NewGuid();
                rows.Add(fourth);
                break;
            case "missing-second":
                rows.RemoveAll(f => f.FiscalPeriod == SecFiscalPeriod.Q2);
                break;
            case "statement-table-quarters":
                foreach (var quarter in rows.Where(f => f.FiscalPeriod != SecFiscalPeriod.FullYear))
                {
                    quarter.Source = FinancialFactSource.ReportedStatement;
                    quarter.Form = StatementTableForms.SixK;
                }
                break;
            case "other-unit":
                rows.Single(f => f.FiscalPeriod == SecFiscalPeriod.Q3).Unit = "EUR";
                break;
            case "other-concept":
                rows.Single(f => f.FiscalPeriod == SecFiscalPeriod.Q2).FinancialConceptId =
                    Guid.NewGuid();
                break;
        }

        DerivedFourthQuarter
            .Append(rows, rejectNegative: shape == "negative")
            .Should()
            .HaveCount(rows.Count);
    }

    private List<FinancialFact> Calendar(decimal year) =>
        [
            Fact("2025-01-01", "2025-03-31", 30m, SecFiscalPeriod.Q1, "USD"),
            Fact("2025-04-01", "2025-06-30", 25m, SecFiscalPeriod.Q2, "USD"),
            Fact("2025-07-01", "2025-09-30", 30m, SecFiscalPeriod.Q3, "USD"),
            Fact("2025-01-01", "2025-12-31", year, SecFiscalPeriod.FullYear, "USD"),
        ];

    private FinancialFact Fact(
        string start,
        string end,
        decimal value,
        SecFiscalPeriod period,
        string unit
    ) =>
        new()
        {
            EquityIssuerId = _stockId,
            FinancialConceptId = _conceptId,
            Unit = unit,
            PeriodType = FactPeriodType.Duration,
            PeriodStart = DateOnly.Parse(start),
            PeriodEnd = DateOnly.Parse(end),
            Value = value,
            FiscalYear = 2025,
            FiscalPeriod = period,
            FiledDate =
                period == SecFiscalPeriod.FullYear
                    ? new DateOnly(2026, 2, 20)
                    : DateOnly.Parse(end).AddDays(30),
            Form = period == SecFiscalPeriod.FullYear ? DocumentType.TenK : DocumentType.TenQ,
            AccessionNumber = $"{period}-{unit}",
        };
}
