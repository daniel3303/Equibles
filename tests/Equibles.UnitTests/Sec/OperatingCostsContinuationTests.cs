using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// When operating costs continue another cost line. Figures are EBS's in millions: it tagged
/// OperatingExpenses through FY2019 and OperatingCostsAndExpenses from FY2018, the same figure in
/// FY2018, FY2019 and FY2019 Q1–Q3, and only OperatingCostsAndExpenses from FY2020.
/// </summary>
public class OperatingCostsContinuationTests
{
    private static readonly Guid OperatingCostsId = Guid.NewGuid();
    private static readonly Guid OperatingExpensesId = Guid.NewGuid();

    private static FinancialFact Fact(
        Guid conceptId,
        DateOnly start,
        DateOnly end,
        decimal millions,
        bool derived = false
    ) =>
        new()
        {
            FinancialConceptId = conceptId,
            Unit = "USD",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = start,
            PeriodEnd = end,
            Value = millions * 1_000_000m,
            Form = derived ? null : DocumentType.TenK,
            AccessionNumber = derived ? null : "0001367644-20-000010",
            FiledDate = new DateOnly(2020, 2, 25),
            DimensionsKey = "",
        };

    private static FinancialFact Year(Guid conceptId, int year, decimal millions) =>
        Fact(conceptId, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), millions);

    private static FinancialFact Quarter(Guid conceptId, int year, int quarter, decimal millions) =>
        Fact(
            conceptId,
            new DateOnly(year, quarter * 3 - 2, 1),
            new DateOnly(year, quarter * 3, 1).AddMonths(1).AddDays(-1),
            millions
        );

    private static List<FinancialFact> EbsOperatingCosts() =>
        [
            Year(OperatingCostsId, 2018, 692.6m),
            Year(OperatingCostsId, 2019, 991.9m),
            Quarter(OperatingCostsId, 2019, 1, 217.8m),
            Quarter(OperatingCostsId, 2019, 2, 250.2m),
            Quarter(OperatingCostsId, 2019, 3, 241.1m),
            Year(OperatingCostsId, 2020, 1_138.8m),
            Year(OperatingCostsId, 2025, 642.8m),
        ];

    private static List<FinancialFact> EbsOperatingExpenses() =>
        [
            Year(OperatingExpensesId, 2017, 436.6m),
            Year(OperatingExpensesId, 2018, 692.6m),
            Year(OperatingExpensesId, 2019, 991.9m),
            Quarter(OperatingExpensesId, 2018, 1, 127.3m),
            Quarter(OperatingExpensesId, 2019, 1, 217.8m),
            Quarter(OperatingExpensesId, 2019, 2, 250.2m),
            Quarter(OperatingExpensesId, 2019, 3, 241.1m),
        ];

    [Fact]
    public void Claimant_EbsTagSwitch_ClaimsOperatingExpenses()
    {
        OperatingCostsContinuation
            .Claimant(EbsOperatingCosts(), EbsOperatingExpenses(), [])
            .Should()
            .Be(OperatingCostsContinuation.OperatingExpensesAlias);
    }

    [Fact]
    public void Claimant_OperatingCostsIncludeCostOfRevenue_ClaimsNothing()
    {
        // The usual filer: operating costs carry cost of revenue, operating expenses do not.
        List<FinancialFact> operatingExpenses =
        [
            Year(OperatingExpensesId, 2018, 300m),
            Year(OperatingExpensesId, 2019, 400m),
        ];

        OperatingCostsContinuation
            .Claimant(EbsOperatingCosts(), operatingExpenses, [])
            .Should()
            .BeNull();
    }

    [Fact]
    public void Claimant_OneDifferingSpan_ClaimsNothing()
    {
        var operatingExpenses = EbsOperatingExpenses();
        operatingExpenses.Add(Quarter(OperatingExpensesId, 2020, 1, 150m));
        var operatingCosts = EbsOperatingCosts();
        operatingCosts.Add(Quarter(OperatingCostsId, 2020, 1, 204.1m));

        OperatingCostsContinuation
            .Claimant(operatingCosts, operatingExpenses, [])
            .Should()
            .BeNull();
    }

    [Fact]
    public void Claimant_OneSharedSpanAndNewPeriods_ClaimsNothing()
    {
        OperatingCostsContinuation
            .Claimant(
                [Year(OperatingCostsId, 2019, 991.9m), Year(OperatingCostsId, 2020, 1_138.8m)],
                [Year(OperatingExpensesId, 2019, 991.9m)],
                []
            )
            .Should()
            .BeNull();
    }

    [Fact]
    public void Claimant_DuplicateOfEverySpan_IsClaimedOnOneSpan()
    {
        OperatingCostsContinuation
            .Claimant(
                [Year(OperatingCostsId, 2019, 991.9m)],
                [Year(OperatingExpensesId, 2018, 692.6m), Year(OperatingExpensesId, 2019, 991.9m)],
                []
            )
            .Should()
            .Be(OperatingCostsContinuation.OperatingExpensesAlias);
    }

    [Fact]
    public void Claimant_DerivedSpansNeverCountAsOverlaps()
    {
        var derivedQ4 = Fact(
            OperatingCostsId,
            new DateOnly(2019, 10, 1),
            new DateOnly(2019, 12, 31),
            282.8m,
            derived: true
        );
        var derivedSiblingQ4 = Fact(
            OperatingExpensesId,
            new DateOnly(2019, 10, 1),
            new DateOnly(2019, 12, 31),
            282.8m,
            derived: true
        );

        OperatingCostsContinuation
            .Claimant(
                [
                    Year(OperatingCostsId, 2019, 991.9m),
                    derivedQ4,
                    Year(OperatingCostsId, 2020, 1_138.8m),
                ],
                [Year(OperatingExpensesId, 2019, 991.9m), derivedSiblingQ4],
                []
            )
            .Should()
            .BeNull();
    }

    [Fact]
    public void Claimant_MatchesTotalCostsWhenOperatingExpensesDiffer()
    {
        var totalCostsId = Guid.NewGuid();

        OperatingCostsContinuation
            .Claimant(
                EbsOperatingCosts(),
                [Year(OperatingExpensesId, 2018, 300m)],
                [Year(totalCostsId, 2018, 692.6m), Year(totalCostsId, 2019, 991.9m)]
            )
            .Should()
            .Be(OperatingCostsContinuation.TotalCostsAlias);
    }

    [Fact]
    public void Fold_Claimant_AppendsOperatingCostsAfterEveryLineConcept()
    {
        var lineFacts = EbsOperatingExpenses();
        var operatingCosts = EbsOperatingCosts();

        var (facts, priority) = OperatingCostsContinuation.Fold(
            OperatingCostsContinuation.OperatingExpensesAlias,
            OperatingCostsContinuation.OperatingExpensesAlias,
            lineFacts,
            new Dictionary<Guid, int> { [OperatingExpensesId] = 0 },
            operatingCosts
        );

        facts.Should().HaveCount(lineFacts.Count + operatingCosts.Count);
        priority[OperatingCostsId].Should().Be(1);
    }

    [Fact]
    public void Fold_OtherLine_LeavesTheLineAlone()
    {
        var lineFacts = EbsOperatingExpenses();

        var (facts, priority) = OperatingCostsContinuation.Fold(
            OperatingCostsContinuation.TotalCostsAlias,
            OperatingCostsContinuation.OperatingExpensesAlias,
            lineFacts,
            new Dictionary<Guid, int> { [OperatingExpensesId] = 0 },
            EbsOperatingCosts()
        );

        facts.Should().Equal(lineFacts);
        priority.Keys.Should().Equal(OperatingExpensesId);
    }
}
