using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The statement Revenue line for banks. Figures are Zions' FY2026 Q2 10-Q in millions: its
/// Revenues tag is a fee slice, so the line reads net interest plus noninterest income; a headline
/// within a tenth of the sum keeps the reported total; a bank with no revenue tag reads the sum.
/// </summary>
public class StatementLineFactsPickFactBankRevenueTests
{
    private static StatementLine Line(string alias) =>
        FinancialStatementConcepts
            .For(FinancialStatementType.IncomeStatement)
            .Single(l => l.Alias == alias);

    private static FinancialFact Quarter(decimal millions, Basis basis = Basis.Reported) =>
        new()
        {
            Unit = "USD",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = new DateOnly(2026, 4, 1),
            PeriodEnd = new DateOnly(2026, 6, 30),
            Value = millions * 1_000_000m,
            FiscalYear = 2026,
            FiscalPeriod = SecFiscalPeriod.Q2,
            Form = basis == Basis.Reported ? DocumentType.TenQ : null,
            FiledDate = new DateOnly(2026, 8, 6),
            AccessionNumber = basis == Basis.Reported ? "0000109380-26-000111" : null,
        };

    private enum Basis
    {
        Reported,
        Derived,
    }

    private static (FinancialConceptAliases.ConceptRef Concept, FinancialFact Fact) Pick(
        string alias,
        params (string Tag, FinancialFact Fact)[] reported
    )
    {
        var conceptIdByKey = new Dictionary<(FactTaxonomy, string), Guid>();
        var facts = new Dictionary<Guid, FinancialFact>();
        foreach (var (tag, fact) in reported)
        {
            var id = Guid.NewGuid();
            conceptIdByKey[(FactTaxonomy.UsGaap, tag)] = id;
            facts[id] = fact;
        }
        return StatementLineFacts.PickConceptFact(Line(alias), conceptIdByKey, facts);
    }

    [Fact]
    public void PickConceptFact_RevenuesIsAFeeSlice_ReadsTheBankSum()
    {
        var (concept, fact) = Pick(
            "revenue",
            ("Revenues", Quarter(182m)),
            ("InterestIncomeExpenseNet", Quarter(677m)),
            ("NoninterestIncome", Quarter(460m))
        );

        fact.Value.Should().Be(1_137_000_000m);
        fact.FinancialConceptId.Should().Be(BankTopLine.ConceptId);
        concept.Should().BeNull();
    }

    [Fact]
    public void PickConceptFact_RevenuesIsTheHeadline_KeepsRevenues()
    {
        var (concept, fact) = Pick(
            "revenue",
            ("Revenues", Quarter(1_100m)),
            ("InterestIncomeExpenseNet", Quarter(677m)),
            ("NoninterestIncome", Quarter(460m))
        );

        fact.Value.Should().Be(1_100_000_000m);
        concept.Tag.Should().Be("Revenues");
    }

    [Fact]
    public void PickConceptFact_NoRevenueTag_ReadsTheBankSum()
    {
        var (_, fact) = Pick(
            "revenue",
            ("InterestIncomeExpenseNet", Quarter(677m)),
            ("NoninterestIncome", Quarter(460m))
        );

        fact.Value.Should().Be(1_137_000_000m);
    }

    [Fact]
    public void PickConceptFact_DerivedComponent_MarksTheSumDerived()
    {
        var (_, fact) = Pick(
            "revenue",
            ("InterestIncomeExpenseNet", Quarter(677m, Basis.Derived)),
            ("NoninterestIncome", Quarter(460m))
        );

        StatementQuarterDerivation.IsDerived(fact).Should().BeTrue();
    }

    [Fact]
    public void PickConceptFact_ComponentLines_StayTheirOwnFigures()
    {
        Pick(
            "net-interest-income",
            ("Revenues", Quarter(182m)),
            ("InterestIncomeExpenseNet", Quarter(677m)),
            ("NoninterestIncome", Quarter(460m))
        )
            .Fact.Value.Should()
            .Be(677_000_000m);
    }

    [Fact]
    public void IsEverReported_BankWithBothComponents_ShowsTheRevenueLine()
    {
        var netInterest = Guid.NewGuid();
        var noninterest = Guid.NewGuid();
        var conceptIdByKey = new Dictionary<(FactTaxonomy, string), Guid>
        {
            [(FactTaxonomy.UsGaap, "InterestIncomeExpenseNet")] = netInterest,
            [(FactTaxonomy.UsGaap, "NoninterestIncome")] = noninterest,
        };

        StatementLineFacts
            .IsEverReported(
                Line("revenue"),
                conceptIdByKey,
                new HashSet<Guid> { netInterest, noninterest }
            )
            .Should()
            .BeTrue();
        StatementLineFacts
            .IsEverReported(Line("revenue"), conceptIdByKey, new HashSet<Guid> { netInterest })
            .Should()
            .BeFalse();
    }
}
