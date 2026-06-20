using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.FinancialFacts.Data;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.UnitTests.Sec;

// Adversarial: a corrupt/typo'd cover-page fact can carry a share count that parses as a decimal
// but exceeds Int64 (the FinancialFact.Value column is numeric, not bigint). Every decimal->long
// cast elsewhere in the codebase is range-checked so an out-of-range value degrades instead of
// throwing (Filing13DGXmlParser.ParseShares: "the decimal->long cast is always range-checked and
// would otherwise throw, crashing the whole filing parse"). GetReportedSharesOutstanding casts the
// selected value with an unguarded (long)value.Value, so a single bad fact crashes the caller with
// an OverflowException. The contract — return the figure "or null when the issuer has none on
// record" — means an unrepresentable figure must be treated as none on record (null), not thrown.
public class SharesOutstandingProviderReportedSharesOverflowTests
{
    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new FinancialFactsTestModuleConfiguration(),
            }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact(
        Skip = "GH-3836 — GetReportedSharesOutstanding throws OverflowException on an out-of-range fact value"
    )]
    public async Task GetReportedSharesOutstanding_FactValueExceedsInt64_DegradesToNullInsteadOfThrowing()
    {
        await using var db = NewDb();
        var stock = new CommonStock
        {
            Ticker = "BIGCO",
            Name = "Overflow Industries",
            Cik = "0009999999",
        };
        var concept = new FinancialConcept
        {
            Taxonomy = FactTaxonomy.Dei,
            Tag = "EntityCommonStockSharesOutstanding",
        };
        db.AddRange(stock, concept);
        // 1e20 shares: parses as a decimal but is larger than long.MaxValue (~9.22e18). A
        // corrupt filing value the provider must not blow up on — it should report "none on
        // record" (null) like any other unusable figure, not surface an OverflowException.
        db.Add(
            Fact(
                stock,
                concept,
                100_000_000_000_000_000_000m,
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 5, 29)
            )
        );
        await db.SaveChangesAsync();

        var provider = new SharesOutstandingProvider(
            new FinancialFactRepository(db),
            new FinancialConceptRepository(db)
        );

        var shares = await provider.GetReportedSharesOutstanding(stock);

        shares.Should().BeNull();
    }

    private static FinancialFact Fact(
        CommonStock stock,
        FinancialConcept concept,
        decimal value,
        DateOnly filed,
        DateOnly asOf
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            FinancialConceptId = concept.Id,
            Unit = "shares",
            PeriodType = FactPeriodType.Instant,
            PeriodStart = asOf,
            PeriodEnd = asOf,
            Value = value,
            FiscalYear = asOf.Year,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            Form = DocumentType.TenQ,
            FiledDate = filed,
            AccessionNumber = $"ACC-{Guid.NewGuid():N}"[..20],
            DimensionsKey = "",
        };
}
