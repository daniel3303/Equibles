using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

public class FinancialFactSeriesShaperPickPerDateTests
{
    private static readonly Guid Cover = Guid.NewGuid();
    private static readonly Guid Sheet = Guid.NewGuid();
    private static readonly Dictionary<Guid, int> Priority = new() { [Cover] = 0, [Sheet] = 1 };

    // A cover-page count and the period-end balance-sheet count share a fiscal stamp; one pick
    // per stamp kept only the later date.
    [Fact]
    public void PickPerDate_TwoDatesUnderOneStamp_PublishesBoth()
    {
        var periodEnd = Count(
            Sheet,
            new DateOnly(2024, 3, 30),
            2024,
            SecFiscalPeriod.Q1,
            100m,
            "2024-05-03",
            "q1"
        );
        var coverDate = Count(
            Cover,
            new DateOnly(2024, 4, 19),
            2024,
            SecFiscalPeriod.Q1,
            99m,
            "2024-05-03",
            "q1"
        );

        var picked = FinancialFactSeriesShaper.PickPerDate([periodEnd, coverDate], Priority);

        picked.Should().BeEquivalentTo([periodEnd, coverDate]);
    }

    // A later filing restating a date wins the value; the date keeps its own filing's stamp, on a
    // copy, because the loaded facts may be EF-tracked.
    [Fact]
    public void PickPerDate_RestatedDate_TakesTheLatestValueUnderTheOwnStamp()
    {
        var original = Count(
            Sheet,
            new DateOnly(2023, 12, 30),
            2023,
            SecFiscalPeriod.FullYear,
            100m,
            "2024-02-02",
            "fy23"
        );
        var recast = Count(
            Sheet,
            new DateOnly(2023, 12, 30),
            2024,
            SecFiscalPeriod.Q1,
            105m,
            "2024-05-03",
            "q1"
        );

        var picked = FinancialFactSeriesShaper.PickPerDate([original, recast], Priority).Single();

        picked.Value.Should().Be(105m);
        picked.AccessionNumber.Should().Be("q1");
        picked.FiscalYear.Should().Be(2023);
        picked.FiscalPeriod.Should().Be(SecFiscalPeriod.FullYear);
        recast.FiscalYear.Should().Be(2024, "the source row is never mutated");
    }

    [Fact]
    public void PickPerDate_AsOriginallyReported_TakesTheEarliestFiling()
    {
        var original = Count(
            Sheet,
            new DateOnly(2023, 12, 30),
            2023,
            SecFiscalPeriod.FullYear,
            100m,
            "2024-02-02",
            "fy23"
        );
        var recast = Count(
            Sheet,
            new DateOnly(2023, 12, 30),
            2024,
            SecFiscalPeriod.Q1,
            105m,
            "2024-05-03",
            "q1"
        );

        var picked = FinancialFactSeriesShaper
            .PickPerDate([recast, original], Priority, asOriginallyReported: true)
            .Single();

        picked.Should().BeSameAs(original);
    }

    // A canonical periodic report outranks a later proxy repeating the date, and the alias's
    // primary tag outranks a fallback tag on the same date.
    [Fact]
    public void PickPerDate_SameDate_RanksSourceThenAliasTag()
    {
        var date = new DateOnly(2024, 6, 30);
        var tenQ = Count(Sheet, date, 2024, SecFiscalPeriod.Q2, 100m, "2024-08-01", "q2");
        var proxy = Count(Sheet, date, 2024, SecFiscalPeriod.Q2, 101m, "2025-03-01", "proxy");
        proxy.Form = DocumentType.Def14A;
        var primary = Count(Cover, date, 2024, SecFiscalPeriod.Q2, 99m, "2024-08-01", "q2");

        FinancialFactSeriesShaper
            .PickPerDate([tenQ, proxy], Priority)
            .Single()
            .Should()
            .BeSameAs(tenQ);
        FinancialFactSeriesShaper
            .PickPerDate([tenQ, primary], Priority)
            .Single()
            .Should()
            .BeSameAs(primary);
    }

    // A span is a flow, never a balance: only points enter a per-date series.
    [Fact]
    public void PickPerDate_IgnoresDurations()
    {
        var point = Count(
            Sheet,
            new DateOnly(2024, 3, 30),
            2024,
            SecFiscalPeriod.Q1,
            100m,
            "2024-05-03",
            "q1"
        );
        var span = Count(
            Sheet,
            new DateOnly(2024, 3, 30),
            2024,
            SecFiscalPeriod.Q1,
            50m,
            "2024-05-03",
            "q1"
        );
        span.PeriodType = FactPeriodType.Duration;
        span.PeriodStart = new DateOnly(2024, 1, 1);

        FinancialFactSeriesShaper.PickPerDate([point, span], Priority).Should().Equal(point);
    }

    private static FinancialFact Count(
        Guid conceptId,
        DateOnly asOf,
        int fiscalYear,
        SecFiscalPeriod fiscalPeriod,
        decimal value,
        string filed,
        string accession
    ) =>
        new()
        {
            EquityIssuerId = Guid.Empty,
            FinancialConceptId = conceptId,
            Unit = "shares",
            PeriodType = FactPeriodType.Instant,
            PeriodStart = asOf,
            PeriodEnd = asOf,
            Value = value,
            FiscalYear = fiscalYear,
            FiscalPeriod = fiscalPeriod,
            Form = DocumentType.TenQ,
            FiledDate = DateOnly.Parse(filed),
            AccessionNumber = accession,
        };
}
