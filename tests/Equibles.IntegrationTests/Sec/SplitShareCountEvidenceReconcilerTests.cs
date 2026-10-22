using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Splits;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Sec;

// The verdict write is a guarded ExecuteUpdate, which the InMemory provider cannot run.
[Collection(ParadeDbCollection.Name)]
public class SplitShareCountEvidenceReconcilerTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    private SplitShareCountEvidenceReconciler Reconciler() =>
        new(
            new StockSplitRepository(DbContext),
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext)
        );

    private async Task<(
        EquityIssuer Issuer,
        StockSplit SpinOff,
        StockSplit UsFactor,
        StockSplit Milan
    )> SeedHoneywell(Guid? issuerId = null)
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: issuerId ?? Guid.NewGuid(),
            Ticker: "HONX"
        );
        var cover = new FinancialConcept
        {
            Taxonomy = FactTaxonomy.Dei,
            Tag = "EntityCommonStockSharesOutstanding",
            Label = "Entity Common Stock, Shares Outstanding",
        };
        DbContext.AddRange(issuer, cover);
        await DbContext.SaveChangesAsync();

        FinancialFact Count(DateOnly asOf, DateOnly filed, decimal shares) =>
            new()
            {
                EquityIssuerId = issuer.Id,
                FinancialConceptId = cover.Id,
                Unit = "shares",
                Value = shares,
                PeriodType = FactPeriodType.Instant,
                PeriodStart = asOf,
                PeriodEnd = asOf,
                FiscalYear = asOf.Year,
                FiscalPeriod = SecFiscalPeriod.Q1,
                Form = DocumentType.TenQ,
                FiledDate = filed,
                AccessionNumber = $"0000773840-{filed:yyMMdd}",
                DimensionsKey = "",
            };
        StockSplit Split(
            DateOnly effective,
            decimal numerator,
            decimal denominator,
            bool presentation
        ) =>
            new()
            {
                EquityIssuerId = issuer.Id,
                EquityListingId = presentation ? issuer.Presentation.EquityListingId : null,
                PriceSeriesTicker = presentation ? "HONX" : "1HONX",
                EffectiveDate = effective,
                Numerator = numerator,
                Denominator = denominator,
                Source = StockSplitSource.Yahoo,
            };

        var spinOff = Split(new DateOnly(2025, 10, 30), 1061m, 1000m, presentation: true);
        var usFactor = Split(new DateOnly(2026, 6, 29), 1907m, 2000m, presentation: true);
        var milan = Split(new DateOnly(2026, 6, 29), 1m, 2m, presentation: false);
        DbContext.AddRange(
            Count(new DateOnly(2025, 9, 30), new DateOnly(2025, 10, 23), 634_887_208m),
            Count(new DateOnly(2026, 1, 23), new DateOnly(2026, 2, 17), 635_675_701m),
            Count(new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 23), 633_653_119m),
            Count(new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 23), 316_940_010m),
            spinOff,
            usFactor,
            milan
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return (issuer, spinOff, usFactor, milan);
    }

    private async Task<StockSplit> Stored(StockSplit split) =>
        await DbContext.Set<StockSplit>().AsNoTracking().SingleAsync(row => row.Id == split.Id);

    [Fact]
    public async Task Reconcile_RecordsEachVerdictOnceAndAgainOnlyWhenItChanges()
    {
        var (_, spinOff, usFactor, milan) = await SeedHoneywell();

        (await Reconciler().Reconcile(CancellationToken.None)).Changed.Should().Be(3);

        (await Stored(spinOff))
            .ShareCountEvidence.Should()
            .Be(StockSplitShareCountEvidence.Contradicted);
        (await Stored(usFactor))
            .ShareCountEvidence.Should()
            .Be(StockSplitShareCountEvidence.Contradicted);
        var confirmed = await Stored(milan);
        confirmed.ShareCountEvidence.Should().Be(StockSplitShareCountEvidence.Confirmed);
        confirmed.ShareCountEvidenceAsOf.Should().Be(new DateOnly(2026, 6, 30));
        confirmed.ShareCountEvidenceTime.Should().NotBeNull();
        (await Reconciler().Reconcile(CancellationToken.None)).Changed.Should().Be(0);
    }

    private async Task<FinancialConcept> SeedCoverConcept()
    {
        var cover = new FinancialConcept
        {
            Taxonomy = FactTaxonomy.Dei,
            Tag = "EntityCommonStockSharesOutstanding",
            Label = "Entity Common Stock, Shares Outstanding",
        };
        DbContext.Add(cover);
        await DbContext.SaveChangesAsync();
        return cover;
    }

    private static FinancialFact ClassCount(
        EquityIssuer issuer,
        FinancialConcept cover,
        DateOnly asOf,
        DateOnly filed,
        string member,
        decimal shares
    ) =>
        new()
        {
            EquityIssuerId = issuer.Id,
            FinancialConceptId = cover.Id,
            Unit = "shares",
            Value = shares,
            PeriodType = FactPeriodType.Instant,
            PeriodStart = asOf,
            PeriodEnd = asOf,
            FiscalYear = asOf.Year,
            FiscalPeriod = SecFiscalPeriod.Q1,
            Form = DocumentType.TenQ,
            FiledDate = filed,
            AccessionNumber = $"0001795250-{filed:yyMMdd}",
            DimensionsKey = $"{member}@{asOf:yyyyMMdd}",
            Dimensions =
            [
                new FinancialFactDimension
                {
                    Axis = "us-gaap:StatementClassOfStockAxis",
                    Member = member,
                },
            ],
        };

    private static StockSplit ListedSplit(
        EquityIssuer issuer,
        string ticker,
        DateOnly effective,
        decimal numerator,
        decimal denominator
    ) =>
        new()
        {
            EquityIssuerId = issuer.Id,
            EquityListingId = issuer.Presentation.EquityListingId,
            PriceSeriesTicker = ticker,
            EffectiveDate = effective,
            Numerator = numerator,
            Denominator = denominator,
            Source = StockSplitSource.Yahoo,
        };

    [Fact]
    public async Task Reconcile_AMultiClassIssuer_IsJudgedOnItsPerClassCountSum()
    {
        // Dual-class filers state only per-class cover counts: Sphere's spin-off factor left both
        // classes unchanged, Alphabet's 20:1 multiplied all three. The roll-up member on the axis
        // must not be summed with the classes it totals.
        var cover = await SeedCoverConcept();
        var sphere = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "SPHX");
        var alphabet = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "GOOX");
        DbContext.AddRange(sphere, alphabet);
        await DbContext.SaveChangesAsync();

        const string classA = "us-gaap:CommonClassAMember";
        const string classB = "us-gaap:CommonClassBMember";
        const string classC = "goog:CapitalClassCMember";
        var spinOff = ListedSplit(sphere, "SPHX", new DateOnly(2023, 4, 21), 2165m, 1000m);
        var twentyForOne = ListedSplit(alphabet, "GOOX", new DateOnly(2022, 7, 18), 20m, 1m);
        DateOnly sphereBefore = new(2023, 1, 31),
            sphereAfter = new(2023, 4, 28),
            alphabetBefore = new(2022, 4, 19),
            alphabetAfter = new(2022, 7, 19);
        DateOnly sphereFiledBefore = new(2023, 2, 9),
            sphereFiledAfter = new(2023, 5, 10),
            alphabetFiledBefore = new(2022, 4, 27),
            alphabetFiledAfter = new(2022, 7, 27);
        DbContext.AddRange(
            ClassCount(sphere, cover, sphereBefore, sphereFiledBefore, classA, 27_687_166m),
            ClassCount(sphere, cover, sphereBefore, sphereFiledBefore, classB, 6_866_754m),
            ClassCount(sphere, cover, sphereAfter, sphereFiledAfter, classA, 27_692_030m),
            ClassCount(sphere, cover, sphereAfter, sphereFiledAfter, classB, 6_866_754m),
            ClassCount(alphabet, cover, alphabetBefore, alphabetFiledBefore, classA, 300_754_904m),
            ClassCount(alphabet, cover, alphabetBefore, alphabetFiledBefore, classB, 44_576_938m),
            ClassCount(alphabet, cover, alphabetBefore, alphabetFiledBefore, classC, 315_639_479m),
            ClassCount(
                alphabet,
                cover,
                alphabetBefore,
                alphabetFiledBefore,
                "us-gaap:CommonStockMember",
                660_971_321m
            ),
            ClassCount(alphabet, cover, alphabetAfter, alphabetFiledAfter, classA, 5_996_000_000m),
            ClassCount(alphabet, cover, alphabetAfter, alphabetFiledAfter, classB, 883_000_000m),
            ClassCount(alphabet, cover, alphabetAfter, alphabetFiledAfter, classC, 6_302_000_000m),
            spinOff,
            twentyForOne
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await Reconciler().Reconcile(CancellationToken.None);

        var contradicted = await Stored(spinOff);
        contradicted.ShareCountEvidence.Should().Be(StockSplitShareCountEvidence.Contradicted);
        contradicted.ShareCountEvidenceAsOf.Should().Be(sphereAfter);
        var confirmed = await Stored(twentyForOne);
        confirmed.ShareCountEvidence.Should().Be(StockSplitShareCountEvidence.Confirmed);
        confirmed.ShareCountEvidenceAsOf.Should().Be(alphabetAfter);
    }

    [Fact]
    public async Task Reconcile_AFilingThatDatesOneClassElsewhere_IsNoWitness()
    {
        // The first post-split 10-Q tags Class B a year early: Class A alone would read as an
        // unchanged count and contradict a real 2:1, so that filing is skipped for the next one.
        var cover = await SeedCoverConcept();
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "DUAL");
        DbContext.Add(issuer);
        await DbContext.SaveChangesAsync();

        const string classA = "us-gaap:CommonClassAMember";
        const string classB = "us-gaap:CommonClassBMember";
        var split = ListedSplit(issuer, "DUAL", new DateOnly(2024, 3, 15), 2m, 1m);
        DateOnly filedBefore = new(2024, 2, 5),
            filedMisdated = new(2024, 5, 6),
            filedNext = new(2024, 8, 5);
        DbContext.AddRange(
            ClassCount(issuer, cover, new DateOnly(2024, 1, 31), filedBefore, classA, 50_000_000m),
            ClassCount(issuer, cover, new DateOnly(2024, 1, 31), filedBefore, classB, 50_000_000m),
            ClassCount(
                issuer,
                cover,
                new DateOnly(2024, 4, 30),
                filedMisdated,
                classA,
                100_000_000m
            ),
            ClassCount(
                issuer,
                cover,
                new DateOnly(2023, 4, 30),
                filedMisdated,
                classB,
                100_000_000m
            ),
            ClassCount(issuer, cover, new DateOnly(2024, 7, 31), filedNext, classA, 100_000_000m),
            ClassCount(issuer, cover, new DateOnly(2024, 7, 31), filedNext, classB, 100_000_000m),
            split
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await Reconciler().Reconcile(CancellationToken.None);

        var judged = await Stored(split);
        judged.ShareCountEvidence.Should().Be(StockSplitShareCountEvidence.Confirmed);
        judged.ShareCountEvidenceAsOf.Should().Be(new DateOnly(2024, 7, 31));
    }

    [Theory]
    [InlineData("ratio")]
    [InlineData("date")]
    [InlineData("source")]
    public async Task ARevisedDefinition_ClearsTheVerdictForEveryWriter(string revision)
    {
        var (_, _, _, milan) = await SeedHoneywell();
        await Reconciler().Reconcile(CancellationToken.None);

        await DbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "StockSplit" SET
                "Denominator" = CASE WHEN {revision} = 'ratio' THEN 3 ELSE "Denominator" END,
                "EffectiveDate" = CASE WHEN {revision} = 'date' THEN "EffectiveDate" + 1 ELSE "EffectiveDate" END,
                "Source" = CASE WHEN {revision} = 'source' THEN 'External' ELSE "Source" END
            WHERE "Id" = {milan.Id}
            """
        );

        var revised = await Stored(milan);
        revised.ShareCountEvidence.Should().BeNull();
        revised.ShareCountEvidenceAsOf.Should().BeNull();
        revised.ShareCountEvidenceTime.Should().BeNull();
    }

    [Fact]
    public async Task Reconcile_AnIssuerTheDatabaseRefuses_DoesNotStopTheOthers()
    {
        // Issuers run in id order, so the refused one comes first and the other must still be judged.
        var (refused, _, _, _) = await SeedHoneywell(
            Guid.Parse("00000000-0000-0000-0000-000000000001")
        );
        var other = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.Parse("ffffffff-0000-0000-0000-000000000001"),
            Ticker: "OTHR"
        );
        DbContext.Add(other);
        await DbContext.SaveChangesAsync();
        var otherSplit = new StockSplit
        {
            EquityIssuerId = other.Id,
            EquityListingId = other.Presentation.EquityListingId,
            PriceSeriesTicker = "OTHR",
            EffectiveDate = new DateOnly(2024, 6, 3),
            Numerator = 3m,
            Denominator = 1m,
            Source = StockSplitSource.Manual,
        };
        DbContext.Add(otherSplit);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var refuse = $"""
            CREATE FUNCTION refuse_issuer() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW."EquityIssuerId" = '{refused.Id}' THEN RAISE EXCEPTION 'refused'; END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER "TR_Test_RefuseIssuer" BEFORE UPDATE ON "StockSplit"
                FOR EACH ROW EXECUTE FUNCTION refuse_issuer();
            """;
        await DbContext.Database.ExecuteSqlRawAsync(refuse);
        try
        {
            var pass = await Reconciler().Reconcile(CancellationToken.None);

            pass.Failures.Select(failure => failure.IssuerId).Should().Equal(refused.Id);
            pass.Changed.Should().Be(1);
            (await Stored(otherSplit))
                .ShareCountEvidence.Should()
                .Be(StockSplitShareCountEvidence.Confirmed);
        }
        finally
        {
            await DbContext.Database.ExecuteSqlRawAsync(
                """
                DROP TRIGGER "TR_Test_RefuseIssuer" ON "StockSplit";
                DROP FUNCTION refuse_issuer();
                """
            );
        }
    }

    [Fact]
    public async Task SetShareCountEvidence_ARowWhoseRatioChangedSinceItWasJudged_IsLeftAlone()
    {
        var (_, _, _, milan) = await SeedHoneywell();
        var judged = await Stored(milan);
        await DbContext
            .Set<StockSplit>()
            .Where(row => row.Id == milan.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Denominator, 3m));

        var written = await new StockSplitRepository(DbContext).SetShareCountEvidence(
            judged,
            StockSplitShareCountEvidence.Confirmed,
            new DateOnly(2026, 6, 30),
            DateTime.UtcNow,
            CancellationToken.None
        );

        written.Should().Be(0);
        (await Stored(milan)).ShareCountEvidence.Should().BeNull();
    }
}
