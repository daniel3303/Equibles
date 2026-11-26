using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The diluted EPS alias reads a continuing-operations figure where the filer tags no other.
/// Freeport-McMoRan's 10-Qs tag 2025's quarters as <c>EarningsPerShareDiluted</c> (0.24, 0.53,
/// 0.46; nine months 1.24) and its 10-K the year only as
/// <c>IncomeLossFromContinuingOperationsPerDilutedShare</c> (1.52). A fact under the
/// <see cref="Constructed"/> accession is made up for its test.
/// </summary>
public class ContinuingOperationsEpsTests
{
    private static readonly Guid Diluted = Guid.NewGuid();
    private static readonly Guid Continuing = Guid.NewGuid();
    private static readonly Guid Discontinued = Guid.NewGuid();
    private static readonly Guid DiscontinuedPerShare = Guid.NewGuid();

    private static readonly ContinuingOperationsEps.Concepts Concepts = new([
        (Diluted, FactTaxonomy.UsGaap, "EarningsPerShareDiluted"),
        (Continuing, FactTaxonomy.UsGaap, "IncomeLossFromContinuingOperationsPerDilutedShare"),
        (Discontinued, FactTaxonomy.UsGaap, "IncomeLossFromDiscontinuedOperationsNetOfTax"),
        (
            DiscontinuedPerShare,
            FactTaxonomy.UsGaap,
            "IncomeLossFromDiscontinuedOperationsNetOfTaxPerDilutedShare"
        ),
    ]);

    private static readonly Dictionary<Guid, int> FamilyPriority = new() { [Diluted] = 0 };

    private const string Constructed = "0000000000-00-000000";
    private const string FreeportAnnual = "0000831259-26-000012";
    private const string FreeportThirdQuarterReport = "0000831259-25-000050";

    private static FinancialFact Fact(
        Guid concept,
        decimal value,
        string start,
        string end,
        string accession,
        string filed,
        string unit = "USD/shares",
        string dimensionsKey = ""
    )
    {
        var periodStart = DateOnly.Parse(start);
        var periodEnd = DateOnly.Parse(end);
        var annual = periodEnd.DayNumber - periodStart.DayNumber > 300;
        return new FinancialFact
        {
            Id = Guid.NewGuid(),
            FinancialConceptId = concept,
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

    private static FinancialFact FreeportYear(
        Guid concept,
        decimal value,
        string unit = "USD/shares"
    ) => Fact(concept, value, "2025-01-01", "2025-12-31", FreeportAnnual, "2026-02-13", unit);

    private static readonly FinancialFact[] FreeportQuarters =
    [
        Fact(Diluted, 0.46m, "2025-07-01", "2025-09-30", FreeportThirdQuarterReport, "2025-11-06"),
        Fact(Diluted, 1.24m, "2025-01-01", "2025-09-30", FreeportThirdQuarterReport, "2025-11-06"),
    ];

    [Fact]
    public void Fold_YearTaggedOnlyAsContinuingOperations_ReadsItAsTheYearsEps()
    {
        var year = FreeportYear(Continuing, 1.52m);

        var (facts, priority) = ContinuingOperationsEps.Fold(
            FreeportQuarters,
            [year],
            Concepts,
            FamilyPriority
        );

        facts.Should().BeEquivalentTo([.. FreeportQuarters, year]);
        priority[Continuing].Should().BeGreaterThan(priority[Diluted]);
    }

    [Theory]
    [InlineData(-0.12, false)]
    [InlineData(0, true)]
    public void Fold_FilingStatesDiscontinuedOperations_KeepsNoContinuingShare(
        double discontinuedPerShare,
        bool read
    )
    {
        var year = FreeportYear(Continuing, 1.52m);
        var discontinued = FreeportYear(DiscontinuedPerShare, (decimal)discontinuedPerShare);

        var (facts, _) = ContinuingOperationsEps.Fold(
            FreeportQuarters,
            [year, discontinued],
            Concepts,
            FamilyPriority
        );

        facts.Contains(year).Should().Be(read);
        facts.Should().NotContain(discontinued);
    }

    // A discontinued figure in dollars refuses the year as its per-share figure does.
    [Fact]
    public void Fold_FilingStatesDiscontinuedIncome_KeepsNoContinuingShare()
    {
        var year = FreeportYear(Continuing, 1.52m);
        var discontinued = FreeportYear(Discontinued, -150_000_000m, unit: "USD");

        var (facts, _) = ContinuingOperationsEps.Fold(
            FreeportQuarters,
            [year, discontinued],
            Concepts,
            FamilyPriority
        );

        facts.Should().BeEquivalentTo(FreeportQuarters);
    }

    // Discontinued operations a later filing reports for the year refuse only that filing's figure.
    [Fact]
    public void Fold_LaterFilingRecastsTheYearWithDiscontinuedOperations_KeepsTheOriginalYear()
    {
        var year = FreeportYear(Continuing, 1.52m);
        var recast = Fact(Continuing, 1.60m, "2025-01-01", "2025-12-31", Constructed, "2027-02-12");
        var recastDiscontinued = Fact(
            DiscontinuedPerShare,
            -0.08m,
            "2025-01-01",
            "2025-12-31",
            Constructed,
            "2027-02-12"
        );

        var (facts, _) = ContinuingOperationsEps.Fold(
            FreeportQuarters,
            [year, recast, recastDiscontinued],
            Concepts,
            FamilyPriority
        );

        facts.Should().Contain(year).And.NotContain(recast);
    }

    [Fact]
    public void Fold_AliasStatesThePeriod_AddsNothing()
    {
        var stated = FreeportYear(Diluted, 1.50m);
        var year = FreeportYear(Continuing, 1.52m);

        var (facts, priority) = ContinuingOperationsEps.Fold(
            [.. FreeportQuarters, stated],
            [year],
            Concepts,
            FamilyPriority
        );

        facts.Should().BeEquivalentTo([.. FreeportQuarters, stated]);
        priority.Should().NotContainKey(Continuing);
    }

    [Fact]
    public void Fold_DimensionedContinuingFigure_IsNoConsolidatedEps()
    {
        var segment = Fact(
            Continuing,
            1.52m,
            "2025-01-01",
            "2025-12-31",
            FreeportAnnual,
            "2026-02-13",
            dimensionsKey: "us-gaap:StatementClassOfStockAxis=fcx:ClassBMember"
        );

        var (facts, _) = ContinuingOperationsEps.Fold(
            FreeportQuarters,
            [segment],
            Concepts,
            FamilyPriority
        );

        facts.Should().BeEquivalentTo(FreeportQuarters);
    }
}
