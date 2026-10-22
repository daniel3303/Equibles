using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The statement Gross Profit line when the filer tagged its operating income as gross profit.
/// Figures are EBS's FY2017 Q3: GrossProfit 47.8M beside OperatingIncomeLoss 47,769,000, while
/// revenue 149.4M less cost of revenue 44,503,000 is 104,897,000.
/// </summary>
public class StatementLineFactsPickFactMistaggedGrossProfitTests
{
    private static StatementLine Line(string alias) =>
        FinancialStatementConcepts
            .For(FinancialStatementType.IncomeStatement)
            .Single(l => l.Alias == alias);

    private static FinancialFact Quarter(decimal value, DateOnly? start = null) =>
        new()
        {
            Unit = "USD",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = start ?? new DateOnly(2017, 7, 1),
            PeriodEnd = new DateOnly(2017, 9, 30),
            Value = value,
            FiscalYear = 2017,
            FiscalPeriod = SecFiscalPeriod.Q3,
            Form = DocumentType.TenQ,
            FiledDate = new DateOnly(2018, 11, 2),
            AccessionNumber = "0001367644-18-000040",
            DimensionsKey = "",
        };

    private static (
        Dictionary<(FactTaxonomy, string), Guid> ConceptIdByKey,
        Dictionary<Guid, FinancialFact> Facts
    ) Period(params (string Tag, FinancialFact Fact)[] reported)
    {
        var conceptIdByKey = new Dictionary<(FactTaxonomy, string), Guid>();
        var facts = new Dictionary<Guid, FinancialFact>();
        foreach (var (tag, fact) in reported)
        {
            var id = Guid.NewGuid();
            conceptIdByKey[(FactTaxonomy.UsGaap, tag)] = id;
            fact.FinancialConceptId = id;
            facts[id] = fact;
        }
        return (conceptIdByKey, facts);
    }

    private static (
        Dictionary<(FactTaxonomy, string), Guid> ConceptIdByKey,
        Dictionary<Guid, FinancialFact> Facts
    ) Ebs(decimal grossProfit = 47_800_000m, DateOnly? incomeStart = null) =>
        Period(
            ("SalesRevenueNet", Quarter(149_400_000m)),
            ("CostOfGoodsAndServicesSold", Quarter(44_503_000m)),
            ("GrossProfit", Quarter(grossProfit)),
            ("OperatingIncomeLoss", Quarter(47_769_000m, incomeStart))
        );

    [Fact]
    public void PickConceptFact_GrossProfitIsTheOperatingIncome_ReadsRevenueLessCost()
    {
        var (conceptIdByKey, facts) = Ebs();

        var (concept, fact) = StatementLineFacts.PickConceptFact(
            Line("gross-profit"),
            conceptIdByKey,
            facts
        );

        fact.Value.Should().Be(104_897_000m);
        StatementQuarterDerivation.IsDerived(fact).Should().BeTrue();
        fact.PeriodStart.Should().Be(new DateOnly(2017, 7, 1));
        concept.Tag.Should().Be("GrossProfit");
        StatementLineFacts
            .ReplacesMistaggedGrossProfit(Line("gross-profit"), conceptIdByKey, facts)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void PickConceptFact_ReplacingNeverTouchesTheStoredFact()
    {
        var (conceptIdByKey, facts) = Ebs();
        var stored = facts[conceptIdByKey[(FactTaxonomy.UsGaap, "GrossProfit")]];

        StatementLineFacts.PickConceptFact(Line("gross-profit"), conceptIdByKey, facts);

        stored.Value.Should().Be(47_800_000m);
        stored.Form.Should().Be(DocumentType.TenQ);
    }

    [Fact]
    public void PickConceptFact_GenuineGrossProfit_KeepsTheTaggedFigure()
    {
        var (conceptIdByKey, facts) = Ebs(grossProfit: 104_897_000m);

        var (_, fact) = StatementLineFacts.PickConceptFact(
            Line("gross-profit"),
            conceptIdByKey,
            facts
        );

        fact.Value.Should().Be(104_897_000m);
        StatementQuarterDerivation.IsDerived(fact).Should().BeFalse();
        StatementLineFacts
            .ReplacesMistaggedGrossProfit(Line("gross-profit"), conceptIdByKey, facts)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void PickConceptFact_OperatingIncomeOverAnotherSpan_KeepsTheTaggedFigure()
    {
        var (conceptIdByKey, facts) = Ebs(incomeStart: new DateOnly(2017, 1, 1));

        var (_, fact) = StatementLineFacts.PickConceptFact(
            Line("gross-profit"),
            conceptIdByKey,
            facts
        );

        fact.Value.Should().Be(47_800_000m);
    }

    [Fact]
    public void PickConceptFact_NoCostOfRevenue_KeepsTheTaggedFigure()
    {
        var (conceptIdByKey, facts) = Period(
            ("SalesRevenueNet", Quarter(149_400_000m)),
            ("GrossProfit", Quarter(47_800_000m)),
            ("OperatingIncomeLoss", Quarter(47_769_000m))
        );

        var (_, fact) = StatementLineFacts.PickConceptFact(
            Line("gross-profit"),
            conceptIdByKey,
            facts
        );

        fact.Value.Should().Be(47_800_000m);
    }

    // EBS FY2025: only OperatingCostsAndExpenses is tagged, and its history proves it the
    // operating-expenses line.
    [Fact]
    public void PickConceptFact_ClaimedOperatingCosts_FillTheClaimantAndDropTheirOwnLine()
    {
        var (conceptIdByKey, facts) = Period(("OperatingCostsAndExpenses", Quarter(642_800_000m)));

        var (concept, claimed) = StatementLineFacts.PickConceptFact(
            Line(OperatingCostsContinuation.OperatingExpensesAlias),
            conceptIdByKey,
            facts,
            OperatingCostsContinuation.OperatingExpensesAlias
        );
        var (_, own) = StatementLineFacts.PickConceptFact(
            Line(OperatingCostsContinuation.OperatingCostsAlias),
            conceptIdByKey,
            facts,
            OperatingCostsContinuation.OperatingExpensesAlias
        );

        claimed.Value.Should().Be(642_800_000m);
        concept.Tag.Should().Be("OperatingCostsAndExpenses");
        own.Should().BeNull();
    }

    [Fact]
    public void PickConceptFact_UnclaimedOperatingCosts_KeepTheirOwnLine()
    {
        var (conceptIdByKey, facts) = Period(("OperatingCostsAndExpenses", Quarter(642_800_000m)));

        var (_, other) = StatementLineFacts.PickConceptFact(
            Line(OperatingCostsContinuation.OperatingExpensesAlias),
            conceptIdByKey,
            facts
        );
        var (_, own) = StatementLineFacts.PickConceptFact(
            Line(OperatingCostsContinuation.OperatingCostsAlias),
            conceptIdByKey,
            facts
        );

        other.Should().BeNull();
        own.Value.Should().Be(642_800_000m);
    }
}
