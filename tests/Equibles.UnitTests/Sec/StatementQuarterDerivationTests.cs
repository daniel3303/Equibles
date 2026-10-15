using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

public class StatementQuarterDerivationTests
{
    private readonly Guid _stockId = Guid.NewGuid();
    private readonly Guid _conceptId = Guid.NewGuid();

    [Theory]
    [InlineData(80_200_000, 156_600_000, 76_400_000)]
    [InlineData(23_000_000, 7_000_000, -16_000_000)]
    public void AppendDerived_Q2CumulativeChain_ProvidesDiscreteQuarter(
        decimal firstQuarter,
        decimal firstHalf,
        decimal expected
    )
    {
        var facts = new[]
        {
            Fact("2026-01-01", "2026-03-31", firstQuarter, SecFiscalPeriod.Q1, "q1"),
            Fact("2026-01-01", "2026-06-30", firstHalf, SecFiscalPeriod.Q2, "q2"),
        };

        var derived = StatementQuarterDerivation
            .AppendDerived(facts)
            .Single(StatementQuarterDerivation.IsDerived);

        derived.Value.Should().Be(expected);
        derived.PeriodStart.Should().Be(new DateOnly(2026, 4, 1));
        derived.PeriodEnd.Should().Be(new DateOnly(2026, 6, 30));
        derived.FiscalYear.Should().Be(2026);
        derived.FiscalPeriod.Should().Be(SecFiscalPeriod.Q2);
    }

    // Interim reporters outside the US (CNINFO, ESEF) state cumulative spans in their own currency.
    [Theory]
    [InlineData("CNY")]
    [InlineData("EUR")]
    [InlineData("HKD")]
    public void AppendDerived_NonUsdCurrencyChain_ProvidesDiscreteQuarterInThatCurrency(string unit)
    {
        var facts = new[]
        {
            Fact("2026-01-01", "2026-03-31", 54_702_912_400m, SecFiscalPeriod.Q1, "q1", unit),
            Fact("2026-01-01", "2026-06-30", 92_278_072_100m, SecFiscalPeriod.Q2, "h1", unit),
        };

        var derived = StatementQuarterDerivation
            .AppendDerived(facts)
            .Single(StatementQuarterDerivation.IsDerived);

        derived.Value.Should().Be(37_575_159_700m);
        derived.Unit.Should().Be(unit);
        derived.PeriodStart.Should().Be(new DateOnly(2026, 4, 1));
    }

    // A count or a per-share figure does not subtract into a quarter, whatever its unit spells.
    [Theory]
    [InlineData("EUR/shares")]
    [InlineData("shares")]
    [InlineData("ADS")]
    [InlineData("pure")]
    [InlineData("LBS")]
    public void AppendDerived_NonCurrencyUnit_RefusesSubtraction(string unit)
    {
        var facts = new[]
        {
            Fact("2026-01-01", "2026-03-31", 80m, SecFiscalPeriod.Q1, "q1", unit),
            Fact("2026-01-01", "2026-06-30", 156m, SecFiscalPeriod.Q2, "h1", unit),
        };

        StatementQuarterDerivation
            .AppendDerived(facts)
            .Should()
            .NotContain(fact => StatementQuarterDerivation.IsDerived(fact));
    }

    [Fact]
    public void AppendDerived_CumulativeFactsInDifferentCurrencies_RefusesSubtraction()
    {
        var facts = new[]
        {
            Fact("2026-01-01", "2026-03-31", 80m, SecFiscalPeriod.Q1, "q1", "EUR"),
            Fact("2026-01-01", "2026-06-30", 156m, SecFiscalPeriod.Q2, "h1", "USD"),
        };

        StatementQuarterDerivation
            .AppendDerived(facts)
            .Should()
            .NotContain(fact => StatementQuarterDerivation.IsDerived(fact));
    }

    [Fact]
    public void AppendDerived_Q3CumulativeChain_ProvidesDiscreteQuarter()
    {
        var facts = new[]
        {
            Fact("2026-01-01", "2026-06-30", 156m, SecFiscalPeriod.Q2, "h1"),
            Fact("2026-01-01", "2026-09-30", 241m, SecFiscalPeriod.Q3, "nine-months"),
        };

        var derived = StatementQuarterDerivation
            .AppendDerived(facts)
            .Single(StatementQuarterDerivation.IsDerived);

        derived.Value.Should().Be(85m);
        derived.PeriodStart.Should().Be(new DateOnly(2026, 7, 1));
        derived.PeriodEnd.Should().Be(new DateOnly(2026, 9, 30));
        derived.FiscalPeriod.Should().Be(SecFiscalPeriod.Q3);
    }

    [Fact]
    public void AppendDerived_ReportedDiscreteQuarterExists_DoesNotAddAnother()
    {
        var facts = new[]
        {
            Fact("2026-01-01", "2026-03-31", 80m, SecFiscalPeriod.Q1, "q1"),
            Fact("2026-01-01", "2026-06-30", 156m, SecFiscalPeriod.Q2, "h1"),
            Fact("2026-04-01", "2026-06-30", 75m, SecFiscalPeriod.Q2, "reported-q2"),
        };

        var result = StatementQuarterDerivation.AppendDerived(facts);

        result.Should().HaveCount(facts.Length);
        result.Count(StatementQuarterDerivation.IsDerived).Should().Be(0);
    }

    [Fact]
    public void AppendDerived_DifferentFiscalYearStart_RefusesSubtraction()
    {
        var facts = new[]
        {
            Fact("2026-01-02", "2026-03-31", 80m, SecFiscalPeriod.Q1, "q1"),
            Fact("2026-01-01", "2026-06-30", 156m, SecFiscalPeriod.Q2, "h1"),
        };

        StatementQuarterDerivation
            .AppendDerived(facts)
            .Count(StatementQuarterDerivation.IsDerived)
            .Should()
            .Be(0);
    }

    [Fact]
    public void AppendDerived_DifferentStockOrDimensions_RefusesSubtraction()
    {
        var firstQuarter = Fact("2026-01-01", "2026-03-31", 80m, SecFiscalPeriod.Q1, "q1");
        var firstHalf = Fact("2026-01-01", "2026-06-30", 156m, SecFiscalPeriod.Q2, "h1");

        firstQuarter.EquityIssuerId = Guid.NewGuid();
        firstHalf.DimensionsKey = "segment-a";

        StatementQuarterDerivation
            .AppendDerived([firstQuarter, firstHalf])
            .Count(StatementQuarterDerivation.IsDerived)
            .Should()
            .Be(0);
    }

    [Fact]
    public void AppendDerived_IncompatibleQuarterEndpoint_RefusesSubtraction()
    {
        var facts = new[]
        {
            Fact("2026-01-01", "2026-03-31", 80m, SecFiscalPeriod.Q1, "q1"),
            Fact("2026-01-01", "2026-08-31", 156m, SecFiscalPeriod.Q2, "h1"),
        };

        StatementQuarterDerivation
            .AppendDerived(facts)
            .Count(StatementQuarterDerivation.IsDerived)
            .Should()
            .Be(0);
    }

    [Fact]
    public void AppendDerived_PerShareCumulativeFacts_RefusesSubtraction()
    {
        var facts = new[]
        {
            Fact("2026-01-01", "2026-03-31", 1m, SecFiscalPeriod.Q1, "q1", "USD/shares"),
            Fact("2026-01-01", "2026-06-30", 2m, SecFiscalPeriod.Q2, "h1", "USD/shares"),
        };

        StatementQuarterDerivation
            .AppendDerived(facts)
            .Count(StatementQuarterDerivation.IsDerived)
            .Should()
            .Be(0);
    }

    [Fact]
    public void AppendDerived_NonNegativeConceptWouldProduceNegative_RefusesSubtraction()
    {
        var facts = new[]
        {
            Fact("2026-01-01", "2026-03-31", 23m, SecFiscalPeriod.Q1, "q1"),
            Fact("2026-01-01", "2026-06-30", 7m, SecFiscalPeriod.Q2, "h1"),
        };

        StatementQuarterDerivation
            .AppendDerived(facts, new HashSet<Guid> { _conceptId })
            .Count(StatementQuarterDerivation.IsDerived)
            .Should()
            .Be(0);
    }

    private FinancialFact Fact(
        string start,
        string end,
        decimal value,
        SecFiscalPeriod period,
        string accession,
        string unit = "USD"
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
            FiscalYear = 2026,
            FiscalPeriod = period,
            FiledDate =
                period == SecFiscalPeriod.Q1
                    ? new DateOnly(2026, 4, 22)
                    : new DateOnly(2026, 7, 29),
            AccessionNumber = accession,
        };
}
