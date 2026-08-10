using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactImportQualityFilterTests
{
    private static readonly Guid StockId = Guid.NewGuid();
    private static readonly Guid ConceptId = Guid.NewGuid();

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
            new[] { original, corruptAmendment }.Concat(quarters).ToList()
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

        var result = FinancialFactImportQualityFilter.Apply([original, later]);

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

        var result = FinancialFactImportQualityFilter.Apply([tenK, proxy]);

        result.Rejected.Should().ContainSingle().Which.Should().BeSameAs(proxy);
        result.Accepted.Should().ContainSingle().Which.Should().BeSameAs(tenK);
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

        var result = FinancialFactImportQualityFilter.Apply([proxy]);

        result.Rejected.Should().BeEmpty();
        result.Accepted.Should().ContainSingle().Which.Should().BeSameAs(proxy);
    }

    private static FinancialFact Fact(
        DateOnly start,
        DateOnly end,
        decimal value,
        DocumentType form,
        DateOnly filed,
        string accession
    ) =>
        new()
        {
            CommonStockId = StockId,
            FinancialConceptId = ConceptId,
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
