using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The diluted EPS alias reads a continuing-operations figure where the filer tags no other and
/// its filing proves it the whole. Freeport-McMoRan's 10-Qs tag 2025's quarters as
/// <c>EarningsPerShareDiluted</c> (Q3 0.46; nine months 1.24); its 10-K tags the year only as
/// <c>IncomeLossFromContinuingOperationsPerDilutedShare</c> (1.52), beside continuing income with
/// minority interests and profit both 4,152 million, 1,443 million diluted shares and 2,197 million
/// of income for common shareholders. A fact under the <see cref="Constructed"/> accession, or a
/// value changed from the filing's, is made up for its test.
/// </summary>
public class ContinuingOperationsEpsTests
{
    private static readonly Guid Diluted = Guid.NewGuid();

    private static readonly Dictionary<string, Guid> Ids = new[]
    {
        "IncomeLossFromContinuingOperationsPerDilutedShare",
        "IncomeLossFromDiscontinuedOperationsNetOfTaxPerDilutedShare",
        "IncomeLossFromContinuingOperationsIncludingPortionAttributableToNoncontrollingInterest",
        "ProfitLoss",
        "WeightedAverageNumberOfDilutedSharesOutstanding",
        "NetIncomeLossAvailableToCommonStockholdersDiluted",
    }
        .ToDictionary(tag => tag, _ => Guid.NewGuid())
        .Append(KeyValuePair.Create("EarningsPerShareDiluted", Diluted))
        .ToDictionary();

    private static readonly ContinuingOperationsEps.Concepts Concepts = new(
        Ids.Select(pair => (pair.Value, FactTaxonomy.UsGaap, pair.Key))
    );

    private static readonly Dictionary<Guid, int> FamilyPriority = new() { [Diluted] = 0 };

    private const string Constructed = "0000000000-00-000000";
    private const string FreeportAnnual = "0000831259-26-000012";
    private const string FreeportThirdQuarterReport = "0000831259-25-000050";

    private static FinancialFact Fact(
        string tag,
        decimal value,
        string unit,
        string start = "2025-01-01",
        string end = "2025-12-31",
        string accession = FreeportAnnual,
        string filed = "2026-02-13",
        string dimensionsKey = ""
    )
    {
        var periodStart = DateOnly.Parse(start);
        var periodEnd = DateOnly.Parse(end);
        var annual = periodEnd.DayNumber - periodStart.DayNumber > 300;
        return new FinancialFact
        {
            Id = Guid.NewGuid(),
            FinancialConceptId = Ids[tag],
            Unit = unit,
            PeriodType = FactPeriodType.Duration,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = value,
            FiscalYear = periodEnd.Year,
            FiscalPeriod = annual ? SecFiscalPeriod.FullYear : SecFiscalPeriod.Q3,
            Form = annual ? DocumentType.TenK : DocumentType.TenQ,
            FiledDate = DateOnly.Parse(filed),
            AccessionNumber = accession,
            DimensionsKey = dimensionsKey,
        };
    }

    private static FinancialFact Year(
        decimal value = 1.52m,
        string accession = FreeportAnnual,
        string dimensionsKey = ""
    ) =>
        Fact(
            "IncomeLossFromContinuingOperationsPerDilutedShare",
            value,
            "USD/shares",
            accession: accession,
            dimensionsKey: dimensionsKey
        );

    // The 10-K's other figures for 2025, with the profit as filed unless a test changes it.
    private static FinancialFact[] Proof(decimal profitMillions = 4_152m) =>
        [
            Fact(
                "IncomeLossFromContinuingOperationsIncludingPortionAttributableToNoncontrollingInterest",
                4_152_000_000m,
                "USD"
            ),
            Fact("ProfitLoss", profitMillions * 1_000_000m, "USD"),
            Fact("WeightedAverageNumberOfDilutedSharesOutstanding", 1_443_000_000m, "shares"),
            Fact("NetIncomeLossAvailableToCommonStockholdersDiluted", 2_197_000_000m, "USD"),
        ];

    private static readonly FinancialFact[] FreeportQuarters =
    [
        Fact(
            "EarningsPerShareDiluted",
            0.46m,
            "USD/shares",
            "2025-07-01",
            "2025-09-30",
            FreeportThirdQuarterReport,
            "2025-11-06"
        ),
        Fact(
            "EarningsPerShareDiluted",
            1.24m,
            "USD/shares",
            "2025-01-01",
            "2025-09-30",
            FreeportThirdQuarterReport,
            "2025-11-06"
        ),
    ];

    private static List<FinancialFact> Fold(params FinancialFact[] evidence) =>
        ContinuingOperationsEps.Fold(FreeportQuarters, evidence, Concepts, FamilyPriority).Facts;

    [Fact]
    public void Fold_FilingProvesTheYearTheWhole_ReadsItAsTheYearsEps()
    {
        var year = Year();

        var (facts, priority) = ContinuingOperationsEps.Fold(
            FreeportQuarters,
            [year, .. Proof()],
            Concepts,
            FamilyPriority
        );

        facts.Should().BeEquivalentTo([.. FreeportQuarters, year]);
        priority[year.FinancialConceptId].Should().BeGreaterThan(priority[Diluted]);
    }

    [Theory]
    [InlineData(-0.12, false)]
    [InlineData(0, true)]
    public void Fold_FilingStatesDiscontinuedOperations_KeepsNoContinuingShare(
        double discontinuedPerShare,
        bool read
    )
    {
        var year = Year();
        var discontinued = Fact(
            "IncomeLossFromDiscontinuedOperationsNetOfTaxPerDilutedShare",
            (decimal)discontinuedPerShare,
            "USD/shares"
        );

        Fold([year, discontinued, .. Proof()]).Contains(year).Should().Be(read);
    }

    // No discontinued line is tagged, yet the profit is not the continuing income.
    [Fact]
    public void Fold_ContinuingIncomeIsNotTheProfit_KeepsNoFigure()
    {
        Fold([Year(), .. Proof(profitMillions: 4_310m)]).Should().BeEquivalentTo(FreeportQuarters);
    }

    // A figure filed with the wrong sign does not reconcile with the diluted shares.
    [Fact]
    public void Fold_FigureDoesNotReconcileWithTheDilutedShares_KeepsNoFigure()
    {
        Fold([Year(-1.52m), .. Proof()]).Should().BeEquivalentTo(FreeportQuarters);
    }

    // A later filing that restates the year without its proof refuses only its own figure.
    [Fact]
    public void Fold_LaterFilingWithoutItsProof_KeepsTheProvedYear()
    {
        var year = Year();
        var recast = Year(1.60m, accession: Constructed);

        Fold([year, recast, .. Proof()]).Should().Contain(year).And.NotContain(recast);
    }

    [Fact]
    public void Fold_AliasStatesThePeriod_AddsNothing()
    {
        var stated = Fact("EarningsPerShareDiluted", 1.50m, "USD/shares");
        var year = Year();

        var (facts, priority) = ContinuingOperationsEps.Fold(
            [.. FreeportQuarters, stated],
            [year, .. Proof()],
            Concepts,
            FamilyPriority
        );

        facts.Should().BeEquivalentTo([.. FreeportQuarters, stated]);
        priority.Should().NotContainKey(year.FinancialConceptId);
    }

    [Fact]
    public void Fold_DimensionedContinuingFigure_IsNoConsolidatedEps()
    {
        var segment = Year(dimensionsKey: "us-gaap:StatementClassOfStockAxis=fcx:ClassBMember");

        Fold([segment, .. Proof()]).Should().BeEquivalentTo(FreeportQuarters);
    }
}
