using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The shared variant-selection rule every statement surface uses: the FIRST
/// variant (declaration order) with a fact wins, later variants only fill the
/// gap — so a company reporting both the broad and the narrow tag always shows
/// the broad one, and a company reporting only the narrow variant (ADBE's
/// software R&amp;D) still renders a value instead of a dash.
/// </summary>
public class StatementLineFactsPickFactTests
{
    private static StatementLine RdLine() =>
        FinancialStatementConcepts
            .For(FinancialStatementType.IncomeStatement)
            .Single(l => l.Alias == "research-and-development");

    private static FinancialFact Fact(decimal value) => new() { Value = value };

    [Fact]
    public void PickFact_PreferredTagReported_WinsOverVariant()
    {
        var line = RdLine();
        var genericId = Guid.NewGuid();
        var softwareId = Guid.NewGuid();
        var conceptIdByKey = new Dictionary<(FactTaxonomy, string), Guid>
        {
            [(FactTaxonomy.UsGaap, "ResearchAndDevelopmentExpense")] = genericId,
            [
                (
                    FactTaxonomy.UsGaap,
                    "ResearchAndDevelopmentExpenseSoftwareExcludingAcquiredInProcessCost"
                )
            ] = softwareId,
        };
        var facts = new Dictionary<Guid, FinancialFact>
        {
            [genericId] = Fact(100),
            [softwareId] = Fact(200),
        };

        var picked = StatementLineFacts.PickFact(line, conceptIdByKey, facts);

        picked.Value.Should().Be(100);
    }

    [Fact]
    public void PickFact_OnlyVariantReported_FillsFromVariant()
    {
        var line = RdLine();
        var softwareId = Guid.NewGuid();
        var conceptIdByKey = new Dictionary<(FactTaxonomy, string), Guid>
        {
            [
                (
                    FactTaxonomy.UsGaap,
                    "ResearchAndDevelopmentExpenseSoftwareExcludingAcquiredInProcessCost"
                )
            ] = softwareId,
        };
        var facts = new Dictionary<Guid, FinancialFact> { [softwareId] = Fact(200) };

        var picked = StatementLineFacts.PickFact(line, conceptIdByKey, facts);

        picked.Value.Should().Be(200);
    }

    [Fact]
    public void PickFact_LaterReportedVariant_WinsOverPreferredDerivedVariant()
    {
        var line = RdLine();
        var genericId = Guid.NewGuid();
        var softwareId = Guid.NewGuid();
        var conceptIdByKey = new Dictionary<(FactTaxonomy, string), Guid>
        {
            [(FactTaxonomy.UsGaap, "ResearchAndDevelopmentExpense")] = genericId,
            [
                (
                    FactTaxonomy.UsGaap,
                    "ResearchAndDevelopmentExpenseSoftwareExcludingAcquiredInProcessCost"
                )
            ] = softwareId,
        };
        var derived = new FinancialFact { Value = 100m };
        var reported = new FinancialFact
        {
            Value = 200m,
            Form = Equibles.Sec.Data.Models.DocumentType.TenQ,
            AccessionNumber = "reported",
        };
        var facts = new Dictionary<Guid, FinancialFact>
        {
            [genericId] = derived,
            [softwareId] = reported,
        };

        var picked = StatementLineFacts.PickFact(line, conceptIdByKey, facts);

        picked.Should().BeSameAs(reported);
    }

    [Fact]
    public void PickFact_NothingReported_ReturnsNull()
    {
        var line = RdLine();

        var picked = StatementLineFacts.PickFact(
            line,
            new Dictionary<(FactTaxonomy, string), Guid>(),
            new Dictionary<Guid, FinancialFact>()
        );

        picked.Should().BeNull();
    }

    [Fact]
    public void CollectConceptPairs_ReturnsEveryVariantTag()
    {
        var (taxonomies, tags) = StatementLineFacts.CollectConceptPairs([RdLine()]);

        taxonomies.Should().Contain([FactTaxonomy.UsGaap, FactTaxonomy.IfrsFull]);
        tags.Should()
            .Contain([
                "ResearchAndDevelopmentExpense",
                "ResearchAndDevelopmentExpenseSoftwareExcludingAcquiredInProcessCost",
            ]);
    }

    [Fact]
    public void PickCurrentlyReported_FullYearRejectsAnOverlongDuration()
    {
        var validAnnual = new FinancialFact
        {
            Value = 120m,
            PeriodType = FactPeriodType.Duration,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            FiledDate = new DateOnly(2026, 2, 1),
            AccessionNumber = "annual",
        };
        var inceptionToDate = new FinancialFact
        {
            Value = 9_999m,
            PeriodType = FactPeriodType.Duration,
            PeriodStart = new DateOnly(2020, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            FiledDate = new DateOnly(2026, 3, 1),
            AccessionNumber = "inception",
        };

        StatementLineFacts
            .PickCurrentlyReported([inceptionToDate, validAnnual], SecFiscalPeriod.FullYear)
            .Should()
            .BeSameAs(validAnnual);
        StatementLineFacts
            .PickCurrentlyReported([inceptionToDate], SecFiscalPeriod.FullYear)
            .Should()
            .BeNull("a multi-year duration is not a fiscal-year fallback");
    }

    [Fact]
    public void PickCurrentlyReportedByConcept_RejectedPreferredTagFallsThroughToValidVariant()
    {
        var line = RdLine();
        var genericId = Guid.NewGuid();
        var softwareId = Guid.NewGuid();
        var conceptIdByKey = new Dictionary<(FactTaxonomy, string), Guid>
        {
            [(FactTaxonomy.UsGaap, "ResearchAndDevelopmentExpense")] = genericId,
            [
                (
                    FactTaxonomy.UsGaap,
                    "ResearchAndDevelopmentExpenseSoftwareExcludingAcquiredInProcessCost"
                )
            ] = softwareId,
        };
        var facts = new[]
        {
            new FinancialFact
            {
                FinancialConceptId = genericId,
                Value = 9_999m,
                PeriodType = FactPeriodType.Duration,
                PeriodStart = new DateOnly(2020, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
            },
            new FinancialFact
            {
                FinancialConceptId = softwareId,
                Value = 200m,
                PeriodType = FactPeriodType.Duration,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
            },
        };

        var byConcept = StatementLineFacts.PickCurrentlyReportedByConcept(
            facts,
            SecFiscalPeriod.FullYear
        );
        var picked = StatementLineFacts.PickFact(line, conceptIdByKey, byConcept);

        byConcept.Should().NotContainKey(genericId);
        picked.Should().BeSameAs(facts[1]);
    }

    [Fact]
    public void PickCurrentlyReported_QuarterStampRejectsAnOverlongDuration()
    {
        var validQuarter = new FinancialFact
        {
            Value = 25m,
            PeriodType = FactPeriodType.Duration,
            PeriodStart = new DateOnly(2025, 4, 1),
            PeriodEnd = new DateOnly(2025, 6, 30),
            FiscalPeriod = SecFiscalPeriod.Q2,
        };
        var inceptionToDate = new FinancialFact
        {
            Value = 9_999m,
            PeriodType = FactPeriodType.Duration,
            PeriodStart = new DateOnly(2003, 5, 13),
            PeriodEnd = new DateOnly(2025, 6, 30),
            FiscalPeriod = SecFiscalPeriod.Q2,
        };

        StatementLineFacts
            .PickCurrentlyReported([inceptionToDate, validQuarter], SecFiscalPeriod.Q2)
            .Should()
            .BeSameAs(validQuarter);
        StatementLineFacts
            .PickCurrentlyReported([inceptionToDate], SecFiscalPeriod.Q2)
            .Should()
            .BeNull("a fiscal stamp cannot turn an inception duration into a quarter");
    }

    [Fact]
    public void PickCurrentlyReported_QuarterWithOnlyYearToDateFact_ReturnsNull()
    {
        var yearToDate = new FinancialFact
        {
            Value = 40m,
            PeriodType = FactPeriodType.Duration,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 6, 30),
            FiscalPeriod = SecFiscalPeriod.Q2,
        };

        StatementLineFacts
            .PickCurrentlyReported([yearToDate], SecFiscalPeriod.Q2)
            .Should()
            .BeNull("a cumulative fact must never appear as one discrete quarter");
    }

    [Fact]
    public void PickCurrentlyReported_FullYearPrefersAnnualFilingOverLaterInterimFiling()
    {
        var annual = AnnualFact(619m, DocumentType.TenK, "annual");
        var interim = AnnualFact(319m, DocumentType.TenQ, "interim");
        interim.FiledDate = annual.FiledDate.AddMonths(6);

        StatementLineFacts
            .PickCurrentlyReported([interim, annual], SecFiscalPeriod.FullYear)
            .Should()
            .BeSameAs(annual);
    }

    [Fact]
    public void PickCurrentlyReported_FullYearPrefersInterimFilingOverLaterCurrentReport()
    {
        var interim = AnnualFact(319m, DocumentType.TenQ, "interim");
        var currentReport = AnnualFact(999m, DocumentType.EightK, "current-report");
        currentReport.FiledDate = interim.FiledDate.AddMonths(1);

        StatementLineFacts
            .PickCurrentlyReported([currentReport, interim], SecFiscalPeriod.FullYear)
            .Should()
            .BeSameAs(interim);
    }

    [Fact]
    public void RejectAmbiguousInterimAnnualFacts_OneConflictingDimensionalAnnualValue_RejectsInterimValue()
    {
        var interim = AnnualFact(319m, DocumentType.TenQ, "interim");
        var dimensionalAnnual = AnnualFact(619m, DocumentType.TenK, "annual-dimensional");
        dimensionalAnnual.DimensionsKey = "dimension-key";

        var result = StatementLineFacts.RejectAmbiguousInterimAnnualFacts(
            [interim],
            [interim, dimensionalAnnual],
            SecFiscalPeriod.FullYear
        );

        result.Should().BeEmpty();
    }

    [Fact]
    public void RejectAmbiguousInterimAnnualFacts_ConsolidatedAnnualValueExists_KeepsInterimEvidence()
    {
        var interim = AnnualFact(319m, DocumentType.TenQ, "interim");
        var annual = AnnualFact(619m, DocumentType.TenK, "annual");

        var result = StatementLineFacts.RejectAmbiguousInterimAnnualFacts(
            [interim, annual],
            [interim, annual],
            SecFiscalPeriod.FullYear
        );

        result.Should().Equal(interim, annual);
    }

    [Fact]
    public void RejectAmbiguousInterimAnnualFacts_MultipleDimensionalAnnualValues_KeepsInterimValue()
    {
        var interim = AnnualFact(319m, DocumentType.TenQ, "interim");
        var first = AnnualFact(619m, DocumentType.TenK, "first");
        first.DimensionsKey = "first-dimension";
        var second = AnnualFact(300m, DocumentType.TenK, "second");
        second.DimensionsKey = "second-dimension";

        var result = StatementLineFacts.RejectAmbiguousInterimAnnualFacts(
            [interim],
            [interim, first, second],
            SecFiscalPeriod.FullYear
        );

        result.Should().ContainSingle().Which.Should().BeSameAs(interim);
    }

    private static FinancialFact AnnualFact(decimal value, DocumentType form, string accession) =>
        new()
        {
            FinancialConceptId = Guid.Parse("3d04a0cf-6b33-4efd-b76c-d8d824495a3b"),
            Unit = "USD",
            Value = value,
            PeriodType = FactPeriodType.Duration,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            FiscalPeriod = SecFiscalPeriod.FullYear,
            Form = form,
            FiledDate = new DateOnly(2026, 2, 18),
            AccessionNumber = accession,
        };
}
