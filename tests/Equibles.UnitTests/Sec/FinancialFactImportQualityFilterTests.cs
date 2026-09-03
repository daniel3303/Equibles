using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactImportQualityFilterTests
{
    private static readonly Guid StockId = Guid.NewGuid();
    private static readonly Guid ConceptId = Guid.NewGuid();
    private static readonly Guid ProfitLossConceptId = Guid.NewGuid();
    private static readonly Guid OperatingIncomeConceptId = Guid.NewGuid();

    private static readonly IReadOnlyDictionary<(FactTaxonomy, string), Guid> ConceptIds =
        new Dictionary<(FactTaxonomy, string), Guid>
        {
            [(FactTaxonomy.UsGaap, "NetIncomeLoss")] = ConceptId,
            [(FactTaxonomy.UsGaap, "ProfitLoss")] = ProfitLossConceptId,
            [(FactTaxonomy.UsGaap, "OperatingIncomeLoss")] = OperatingIncomeConceptId,
        };

    [Fact]
    public void Apply_AmendmentExactlyOneThousandTimesPriorAnnualAndCorroboratedByQuarters_RejectsAmendment()
    {
        var original = Fact(
            new DateOnly(2023, 1, 1),
            new DateOnly(2023, 12, 31),
            -107_322_000m,
            DocumentType.TenK,
            new DateOnly(2024, 3, 7),
            "original"
        );
        var corruptAmendment = Fact(
            original.PeriodStart,
            original.PeriodEnd,
            -107_322_000_000m,
            DocumentType.TenKa,
            new DateOnly(2024, 5, 1),
            "amendment"
        );
        var quarters = new[]
        {
            Fact(
                new DateOnly(2023, 1, 1),
                new DateOnly(2023, 3, 31),
                -20_000_000m,
                DocumentType.TenQ,
                new DateOnly(2023, 5, 1),
                "q1"
            ),
            Fact(
                new DateOnly(2023, 4, 1),
                new DateOnly(2023, 6, 30),
                -30_000_000m,
                DocumentType.TenQ,
                new DateOnly(2023, 8, 1),
                "q2"
            ),
            Fact(
                new DateOnly(2023, 7, 1),
                new DateOnly(2023, 9, 30),
                -36_044_000m,
                DocumentType.TenQ,
                new DateOnly(2023, 11, 1),
                "q3"
            ),
        };

        var result = FinancialFactImportQualityFilter.Apply(
            new[] { original, corruptAmendment }.Concat(quarters).ToList(),
            ConceptIds
        );

        result.Rejected.Should().ContainSingle().Which.Should().BeSameAs(corruptAmendment);
        result.Accepted.Should().Contain(original);
        result.Accepted.Should().Contain(quarters);
    }

    [Fact]
    public void Apply_ScaledAnnualWithoutQuarterCorroboration_KeepsBothRows()
    {
        var original = Fact(
            new DateOnly(2023, 1, 1),
            new DateOnly(2023, 12, 31),
            107_322_000m,
            DocumentType.TenK,
            new DateOnly(2024, 3, 7),
            "original"
        );
        var later = Fact(
            original.PeriodStart,
            original.PeriodEnd,
            107_322_000_000m,
            DocumentType.TenKa,
            new DateOnly(2024, 5, 1),
            "amendment"
        );

        var result = FinancialFactImportQualityFilter.Apply([original, later], ConceptIds);

        result.Rejected.Should().BeEmpty();
        result.Accepted.Should().Equal(original, later);
    }

    [Fact]
    public void Apply_ProxyDuplicateAndPeriodicFactShareActualPeriod_RejectsProxyOnly()
    {
        var tenK = Fact(
            new DateOnly(2023, 1, 1),
            new DateOnly(2023, 12, 31),
            1_234_567_890m,
            DocumentType.TenK,
            new DateOnly(2024, 2, 1),
            "ten-k"
        );
        var proxy = Fact(
            tenK.PeriodStart,
            tenK.PeriodEnd,
            1_235_000_000m,
            DocumentType.Def14A,
            new DateOnly(2024, 4, 1),
            "proxy"
        );

        var result = FinancialFactImportQualityFilter.Apply([tenK, proxy], ConceptIds);

        result.Rejected.Should().ContainSingle().Which.Should().BeSameAs(proxy);
        result.Accepted.Should().ContainSingle().Which.Should().BeSameAs(tenK);
    }

    // The RealReal shape (MCP feedback 4cbb4cd8): a PRELIMINARY proxy, whose form name
    // DocumentType does not map, so it is stored as Other and shares the proxy's lowest
    // source rank. It restated FY2025 net income with the sign flipped and a thousand-fold
    // scale, which the scale rule cannot catch (it requires the candidate and the quarter
    // sum to share a sign), and the derived-Q4 synthesis then picked it as the latest-filed
    // annual and published a $41.76B TTM net income for a company that lost $41.8M.
    [Fact]
    public void Apply_UnmappedLowerPriorityFormSharesActualPeriodWithPeriodicFact_RejectsIt()
    {
        var tenK = Fact(
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            -41_799_000m,
            DocumentType.TenK,
            new DateOnly(2026, 2, 26),
            "0001573221-26-000010"
        );
        var preliminaryProxy = Fact(
            tenK.PeriodStart,
            tenK.PeriodEnd,
            41_799_000_000m,
            DocumentType.Other,
            new DateOnly(2026, 4, 15),
            "0001573221-26-000026"
        );

        var result = FinancialFactImportQualityFilter.Apply([tenK, preliminaryProxy], ConceptIds);

        result.Rejected.Should().ContainSingle().Which.Should().BeSameAs(preliminaryProxy);
        result.Accepted.Should().ContainSingle().Which.Should().BeSameAs(tenK);
    }

    // An 8-K ranks ABOVE the proxy tier and states genuine interim figures, so it survives
    // beside a periodic report — the rule rejects the lowest rank only.
    [Fact]
    public void Apply_CurrentReportSharesActualPeriodWithPeriodicFact_KeepsBoth()
    {
        var tenK = Fact(
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            1_000_000m,
            DocumentType.TenK,
            new DateOnly(2026, 2, 26),
            "ten-k"
        );
        var eightK = Fact(
            tenK.PeriodStart,
            tenK.PeriodEnd,
            1_000_000m,
            DocumentType.EightK,
            new DateOnly(2026, 1, 20),
            "eight-k"
        );

        var result = FinancialFactImportQualityFilter.Apply([tenK, eightK], ConceptIds);

        result.Rejected.Should().BeEmpty();
        result.Accepted.Should().Equal(tenK, eightK);
    }

    // Nothing better exists for that period, so the only row stays selectable whatever its
    // form — rejecting it would delete the company's only figure.
    [Fact]
    public void Apply_UnmappedLowerPriorityFormOnlyPeriod_KeepsAvailableFact()
    {
        var proxyOnly = Fact(
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            41_799_000_000m,
            DocumentType.Other,
            new DateOnly(2026, 4, 15),
            "proxy-only"
        );

        var result = FinancialFactImportQualityFilter.Apply([proxyOnly], ConceptIds);

        result.Rejected.Should().BeEmpty();
        result.Accepted.Should().ContainSingle().Which.Should().BeSameAs(proxyOnly);
    }

    [Fact]
    public void Apply_ProxyOnlyPeriod_KeepsAvailableFact()
    {
        var proxy = Fact(
            new DateOnly(2023, 1, 1),
            new DateOnly(2023, 12, 31),
            1_235_000_000m,
            DocumentType.Def14A,
            new DateOnly(2024, 4, 1),
            "proxy"
        );

        var result = FinancialFactImportQualityFilter.Apply([proxy], ConceptIds);

        result.Rejected.Should().BeEmpty();
        result.Accepted.Should().ContainSingle().Which.Should().BeSameAs(proxy);
    }

    [Fact]
    public void Apply_AmendmentCorroboratedByAliasConceptQuarters_RejectsAmendment()
    {
        var original = Fact(
            new DateOnly(2022, 1, 1),
            new DateOnly(2022, 12, 31),
            -142_181_000m,
            DocumentType.TenK,
            new DateOnly(2023, 3, 7),
            "original"
        );
        var corruptAmendment = Fact(
            original.PeriodStart,
            original.PeriodEnd,
            -142_181_000_000m,
            DocumentType.TenKa,
            new DateOnly(2026, 4, 17),
            "amendment"
        );
        var aliasQuarters = new[]
        {
            Fact(
                new DateOnly(2022, 1, 1),
                new DateOnly(2022, 3, 31),
                -35_000_000m,
                DocumentType.TenQ,
                new DateOnly(2022, 5, 1),
                "q1",
                ProfitLossConceptId
            ),
            Fact(
                new DateOnly(2022, 4, 1),
                new DateOnly(2022, 6, 30),
                -45_000_000m,
                DocumentType.TenQ,
                new DateOnly(2022, 8, 1),
                "q2",
                ProfitLossConceptId
            ),
            Fact(
                new DateOnly(2022, 7, 1),
                new DateOnly(2022, 9, 30),
                -40_000_000m,
                DocumentType.TenQ,
                new DateOnly(2022, 11, 1),
                "q3",
                ProfitLossConceptId
            ),
        };

        var result = FinancialFactImportQualityFilter.Apply(
            new[] { original, corruptAmendment }.Concat(aliasQuarters).ToList(),
            ConceptIds
        );

        result.Rejected.Should().ContainSingle().Which.Should().BeSameAs(corruptAmendment);
    }

    [Fact]
    public void Apply_AmendmentWithUnrelatedConceptQuarters_KeepsAmendment()
    {
        var original = Fact(
            new DateOnly(2022, 1, 1),
            new DateOnly(2022, 12, 31),
            -142_181_000m,
            DocumentType.TenK,
            new DateOnly(2023, 3, 7),
            "original"
        );
        var corruptAmendment = Fact(
            original.PeriodStart,
            original.PeriodEnd,
            -142_181_000_000m,
            DocumentType.TenKa,
            new DateOnly(2026, 4, 17),
            "amendment"
        );
        var unrelatedQuarters = new[]
        {
            Fact(
                new DateOnly(2022, 1, 1),
                new DateOnly(2022, 3, 31),
                -35_000_000m,
                DocumentType.TenQ,
                new DateOnly(2022, 5, 1),
                "q1",
                OperatingIncomeConceptId
            ),
            Fact(
                new DateOnly(2022, 4, 1),
                new DateOnly(2022, 6, 30),
                -45_000_000m,
                DocumentType.TenQ,
                new DateOnly(2022, 8, 1),
                "q2",
                OperatingIncomeConceptId
            ),
            Fact(
                new DateOnly(2022, 7, 1),
                new DateOnly(2022, 9, 30),
                -40_000_000m,
                DocumentType.TenQ,
                new DateOnly(2022, 11, 1),
                "q3",
                OperatingIncomeConceptId
            ),
        };

        var result = FinancialFactImportQualityFilter.Apply(
            new[] { original, corruptAmendment }.Concat(unrelatedQuarters).ToList(),
            ConceptIds
        );

        result.Rejected.Should().BeEmpty();
        result.Accepted.Should().Contain(corruptAmendment);
    }

    private static FinancialFact Fact(
        DateOnly start,
        DateOnly end,
        decimal value,
        DocumentType form,
        DateOnly filed,
        string accession,
        Guid? conceptId = null
    ) =>
        new()
        {
            CommonStockId = StockId,
            FinancialConceptId = conceptId ?? ConceptId,
            Unit = "USD",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = start,
            PeriodEnd = end,
            Value = value,
            FiscalYear = end.Year,
            FiscalPeriod =
                end.DayNumber - start.DayNumber >= 350
                    ? SecFiscalPeriod.FullYear
                    : SecFiscalPeriod.Q1,
            Form = form,
            FiledDate = filed,
            AccessionNumber = accession,
        };
}
