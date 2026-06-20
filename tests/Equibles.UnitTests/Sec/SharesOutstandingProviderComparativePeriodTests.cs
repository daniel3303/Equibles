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

// Adversarial: GetSummedPerClassSharesOutstanding is documented to sum the per-class cover-page
// counts "pinned to that one accession and as-of date so classes are never mixed across filings".
// A multi-class filing can carry the same dei:EntityCommonStockSharesOutstanding class-axis concept
// at more than one instant within a SINGLE accession (e.g. a comparative prior-period context). The
// contract says only the latest as-of date's classes form the entity total. The existing suite never
// exercises the "&& f.PeriodEnd == latest.PeriodEnd" clause in isolation: its older filing differs
// by BOTH accession and as-of date, so the accession filter alone already excludes it. This pins the
// as-of-date clause on its own — same accession, two as-of dates — so dropping it (which would mix
// the prior-period classes into the sum) fails here.
public class SharesOutstandingProviderComparativePeriodTests
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

    [Fact]
    public async Task GetSummedPerClassSharesOutstanding_LatestFilingCarriesComparativePriorPeriodRows_SumsOnlyLatestAsOfDate()
    {
        await using var db = NewDb();
        var stock = new CommonStock
        {
            Ticker = "GOOGL",
            Name = "Alphabet",
            Cik = "0001652044",
        };
        var concept = new FinancialConcept
        {
            Taxonomy = FactTaxonomy.Dei,
            Tag = "EntityCommonStockSharesOutstanding",
        };
        db.AddRange(stock, concept);
        const string acc = "0001652044-26-000048";
        // The latest filing's CURRENT as-of date (2026-04-23): Class A + Class B = 6,660,000,000.
        db.Add(
            ClassFact(
                stock,
                concept,
                5_824_000_000m,
                new(2026, 4, 30),
                new(2026, 4, 23),
                acc,
                "us-gaap:CommonClassAMember"
            )
        );
        db.Add(
            ClassFact(
                stock,
                concept,
                836_000_000m,
                new(2026, 4, 30),
                new(2026, 4, 23),
                acc,
                "us-gaap:CommonClassBMember"
            )
        );
        // The SAME accession also carries the prior-period (2025-04-23) class counts as comparatives.
        // Pinned to the latest as-of date, these must never be added to the current entity total.
        db.Add(
            ClassFact(
                stock,
                concept,
                5_671_000_000m,
                new(2026, 4, 30),
                new(2025, 4, 23),
                acc,
                "us-gaap:CommonClassAMember"
            )
        );
        db.Add(
            ClassFact(
                stock,
                concept,
                800_000_000m,
                new(2026, 4, 30),
                new(2025, 4, 23),
                acc,
                "us-gaap:CommonClassBMember"
            )
        );
        await db.SaveChangesAsync();

        var provider = new SharesOutstandingProvider(
            new FinancialFactRepository(db),
            new FinancialConceptRepository(db)
        );

        var shares = await provider.GetSummedPerClassSharesOutstanding(stock);

        shares.Should().Be(6_660_000_000);
    }

    private static FinancialFact ClassFact(
        CommonStock stock,
        FinancialConcept concept,
        decimal value,
        DateOnly filed,
        DateOnly asOf,
        string accession,
        string member,
        string axis = "us-gaap:StatementClassOfStockAxis"
    )
    {
        var fact = new FinancialFact
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
            AccessionNumber = accession,
            DimensionsKey = $"{axis}={member}",
        };
        fact.Dimensions.Add(new FinancialFactDimension { Axis = axis, Member = member });
        return fact;
    }
}
