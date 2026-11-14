using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.FinancialFacts.BusinessLogic.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Sec;

// The representative-fact query and the conflict-ignoring inserts only run on real Postgres.
[Collection(ParadeDbCollection.Name)]
public class FinancialDimensionSetBackfillTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    private static readonly (string Axis, string Member) IPhone = (
        "srt:ProductOrServiceAxis",
        "aapl:IPhoneMember"
    );
    private static readonly (string Axis, string Member) Americas = (
        "srt:StatementGeographicalAxis",
        "aapl:AmericasSegmentMember"
    );

    private FinancialConcept _revenue;
    private int _accession;

    private FinancialDimensionSetBackfill Backfill()
    {
        var setRepository = new FinancialDimensionSetRepository(DbContext);
        return new FinancialDimensionSetBackfill(
            new EquityIssuerRepository(DbContext),
            new FinancialFactRepository(DbContext),
            new FinancialFactDimensionRepository(DbContext),
            setRepository,
            new FinancialDimensionSetWriter(setRepository)
        );
    }

    private static string Key(params (string Axis, string Member)[] pairs) =>
        XbrlDimensionsKey.Compute(
            pairs
                .Select(pair => new ParsedXbrlDimension { Axis = pair.Axis, Member = pair.Member })
                .ToList()
        );

    private async Task<EquityIssuer> SeedIssuer(string ticker)
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: ticker
        );
        _revenue ??= DbContext
            .Add(
                new FinancialConcept
                {
                    Taxonomy = FactTaxonomy.UsGaap,
                    Tag = "Revenues",
                    Label = "Revenues",
                }
            )
            .Entity;
        DbContext.Add(issuer);
        await DbContext.SaveChangesAsync();
        return issuer;
    }

    /// <summary>A fact keyed by <paramref name="set"/> that stores only <paramref name="stored"/> rows.</summary>
    private FinancialFact Fact(
        EquityIssuer issuer,
        (string Axis, string Member)[] set,
        (string Axis, string Member)[] stored = null,
        Guid? id = null
    )
    {
        var fact = DbContext
            .Add(
                new FinancialFact
                {
                    Id = id ?? Guid.NewGuid(),
                    EquityIssuerId = issuer.Id,
                    FinancialConceptId = _revenue.Id,
                    Unit = "USD",
                    Value = 1m,
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = new DateOnly(2025, 1, 1),
                    PeriodEnd = new DateOnly(2025, 3, 31),
                    FiscalYear = 2025,
                    FiscalPeriod = SecFiscalPeriod.Q1,
                    Form = DocumentType.TenQ,
                    FiledDate = new DateOnly(2025, 5, 1),
                    AccessionNumber = $"0000320193-25-{++_accession:D6}",
                    DimensionsKey = Key(set),
                }
            )
            .Entity;
        DbContext.AddRange(
            (stored ?? set).Select(pair => new FinancialFactDimension
            {
                FinancialFactId = fact.Id,
                Axis = pair.Axis,
                Member = pair.Member,
            })
        );
        return fact;
    }

    private async Task<List<FinancialDimensionSet>> StoredSets()
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext
            .Set<FinancialDimensionSet>()
            .Include(set => set.Members)
            .AsNoTracking()
            .ToListAsync();
    }

    [Fact]
    public async Task Run_FactsWrittenBeforeTheSetTable_StoresEachCombinationOnce()
    {
        var apple = await SeedIssuer("AAPL");
        var other = await SeedIssuer("OTHR");
        Fact(apple, [IPhone]);
        Fact(apple, [IPhone]);
        Fact(other, [IPhone]);
        Fact(apple, [IPhone, Americas]);
        await DbContext.SaveChangesAsync();

        var pass = await Backfill().Run(CancellationToken.None);

        pass.Should().Be(new FinancialDimensionSetBackfillPass(Added: 2, Unresolved: 0));
        var sets = await StoredSets();
        sets.Select(set => set.Key).Should().BeEquivalentTo(Key(IPhone), Key(IPhone, Americas));
        sets.Single(set => set.Key == Key(IPhone, Americas))
            .Members.Select(member => (member.Axis, member.Member))
            .Should()
            .BeEquivalentTo([IPhone, Americas]);
        sets.Single(set => set.Key == Key(IPhone)).Members.Should().ContainSingle();

        (await Backfill().Run(CancellationToken.None))
            .Should()
            .Be(new FinancialDimensionSetBackfillPass(Added: 0, Unresolved: 0));
        (await StoredSets()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Run_FirstFactHasRowsCutShort_TakesTheSetFromAnotherFactWithTheKey()
    {
        var apple = await SeedIssuer("AAPL");
        Fact(
            apple,
            [IPhone, Americas],
            stored: [IPhone],
            id: Guid.Parse("00000000-0000-0000-0000-000000000001")
        );
        Fact(apple, [IPhone, Americas], id: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        await DbContext.SaveChangesAsync();

        var pass = await Backfill().Run(CancellationToken.None);

        pass.Should().Be(new FinancialDimensionSetBackfillPass(Added: 1, Unresolved: 0));
        (await StoredSets()).Single().Members.Should().HaveCount(2);
    }

    [Fact]
    public async Task Run_NoFactStatesTheWholeSet_LeavesTheKeyWithoutASet()
    {
        var apple = await SeedIssuer("AAPL");
        Fact(apple, [IPhone, Americas], stored: [IPhone]);
        await DbContext.SaveChangesAsync();

        var pass = await Backfill().Run(CancellationToken.None);

        pass.Should().Be(new FinancialDimensionSetBackfillPass(Added: 0, Unresolved: 1));
        (await StoredSets()).Should().BeEmpty();
    }
}
