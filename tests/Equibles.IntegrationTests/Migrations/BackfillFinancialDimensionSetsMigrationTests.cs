using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Migrations;

// The migrated schema no longer has the per-fact table, so each test restores it, fills it the
// way an install from before the dimension sets holds it, and drops it again.
[Collection(ParadeDbCollection.Name)]
public class BackfillFinancialDimensionSetsMigrationTests(ParadeDbFixture fixture)
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

    // A test that fails midway must still leave the migrated schema, without the table.
    public override async Task DisposeAsync()
    {
        await DbContext.Database.ExecuteSqlRawAsync(DropFinancialFactDimension.DropSql);
        await base.DisposeAsync();
    }

    // The key exactly as the extractor computes it: the SQL must agree with it.
    private static string Key(params (string Axis, string Member)[] pairs) =>
        XbrlDimensionsKey.Compute(
            pairs
                .Select(pair => new ParsedXbrlDimension { Axis = pair.Axis, Member = pair.Member })
                .ToList()
        );

    private async Task<EquityIssuer> SeedIssuer(string ticker)
    {
        await DbContext.Database.ExecuteSqlRawAsync(DropFinancialFactDimension.RestoreSql);
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

    /// <summary>A fact keyed by <paramref name="set"/> whose rows state only <paramref name="stored"/>.</summary>
    private async Task Fact(
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
        await DbContext.SaveChangesAsync();
        foreach (var (axis, member) in stored ?? set)
        {
            await DbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "FinancialFactDimension" ("Id", "FinancialFactId", "Axis", "Member", "CreationTime")
                VALUES ({Guid.NewGuid()}, {fact.Id}, {axis}, {member}, now())
                """
            );
        }
    }

    private async Task<List<FinancialDimensionSet>> BackfilledSets()
    {
        await DbContext.Database.ExecuteSqlRawAsync(BackfillFinancialDimensionSets.BackfillSql);
        await DbContext.Database.ExecuteSqlRawAsync(DropFinancialFactDimension.DropSql);
        DbContext.ChangeTracker.Clear();
        return await DbContext
            .Set<FinancialDimensionSet>()
            .Include(set => set.Members)
            .AsNoTracking()
            .ToListAsync();
    }

    [Fact]
    public async Task Up_FactsStoredBeforeTheSets_StoresEachCombinationOnceUnderTheExtractorsKey()
    {
        var apple = await SeedIssuer("AAPL");
        var other = await SeedIssuer("OTHR");
        await Fact(apple, [IPhone]);
        await Fact(apple, [IPhone]);
        await Fact(other, [IPhone]);
        await Fact(apple, [Americas, IPhone]);

        var sets = await BackfilledSets();

        sets.Select(set => set.Key).Should().BeEquivalentTo(Key(IPhone), Key(IPhone, Americas));
        sets.Single(set => set.Key == Key(IPhone, Americas))
            .Members.Select(member => (member.Axis, member.Member))
            .Should()
            .BeEquivalentTo([IPhone, Americas]);
        sets.Single(set => set.Key == Key(IPhone)).Members.Should().ContainSingle();
    }

    // Byte order puts "C" before "b" and "x:A-1=" before "x:A=": neither a linguistic sort nor
    // the axis order the rows are stored in gives the extractor's key.
    [Fact]
    public async Task Up_PairsWhoseOrdinalOrderDiffersFromOtherOrders_StillHashToTheExtractorsKey()
    {
        (string Axis, string Member)[] pairs =
        [
            ("x:b", "m:Member"),
            ("x:C", "m:Member"),
            ("x:A", "m:Member"),
            ("x:A-1", "m:Member"),
        ];
        var apple = await SeedIssuer("AAPL");
        await Fact(apple, pairs);

        var set = (await BackfilledSets()).Should().ContainSingle().Subject;

        set.Key.Should().Be(Key(pairs));
        set.Members.Select(member => (member.Axis, member.Member)).Should().BeEquivalentTo(pairs);
    }

    [Fact]
    public async Task Up_AFactWithAKeyAndNoRows_GetsNoSet()
    {
        var apple = await SeedIssuer("AAPL");
        await Fact(apple, [IPhone], stored: []);

        (await BackfilledSets()).Should().BeEmpty();
    }

    [Fact]
    public async Task Up_FirstFactHasRowsCutShort_TakesTheSetFromAnotherFactWithTheKey()
    {
        var apple = await SeedIssuer("AAPL");
        await Fact(
            apple,
            [IPhone, Americas],
            stored: [IPhone],
            id: Guid.Parse("00000000-0000-0000-0000-000000000001")
        );
        await Fact(
            apple,
            [IPhone, Americas],
            id: Guid.Parse("00000000-0000-0000-0000-000000000002")
        );

        (await BackfilledSets()).Should().ContainSingle().Which.Members.Should().HaveCount(2);
    }

    [Fact]
    public async Task Up_NoFactStatesTheWholeSet_LeavesTheKeyWithoutASet()
    {
        var apple = await SeedIssuer("AAPL");
        await Fact(apple, [IPhone, Americas], stored: [IPhone]);

        (await BackfilledSets()).Should().BeEmpty();
    }

    [Fact]
    public async Task Up_ASetAlreadyStored_IsLeftAsItIs()
    {
        var apple = await SeedIssuer("AAPL");
        await Fact(apple, [IPhone, Americas]);
        DbContext.Add(
            new FinancialDimensionSet
            {
                Key = Key(IPhone, Americas),
                Members =
                [
                    new FinancialDimensionSetMember
                    {
                        DimensionSetKey = Key(IPhone, Americas),
                        Axis = IPhone.Axis,
                        Member = IPhone.Member,
                    },
                ],
            }
        );
        await DbContext.SaveChangesAsync();

        (await BackfilledSets())
            .Should()
            .ContainSingle()
            .Which.Members.Should()
            .ContainSingle("a stored set is never given members");
    }
}
