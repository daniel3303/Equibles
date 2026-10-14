using Equibles.CommonStocks.BusinessLogic.Directory;
using Equibles.CommonStocks.BusinessLogic.Merge;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class EquityIssuerMergeTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    private const string VenueSource = "https://example.org/venue/securities";

    private static EquityDirectoryListingInput HongKong() =>
        new()
        {
            Source = "venue-directory",
            SourceIssuerIdentifier = "security:09988",
            IssuerName = "BABA-W",
            SourceSecurityIdentifier = "09988",
            SourceListingIdentifier = "09988",
            Isin = "KYG017191142",
            RelatedIsins = ["KYG017191142"],
            Ticker = "09988",
            MarketIdentifierCode = "XHKG",
            MarketCountryCode = "HK",
            TradingCurrency = "HKD",
            QuoteUnitMultiplier = 1m,
            SourceUrl = VenueSource,
            PayloadJson = JsonConvert.SerializeObject(new { code = "09988", name = "BABA-W" }),
        };

    private ServiceProvider Services() =>
        new ServiceCollection()
            .AddScoped<EquiblesFinancialDbContext>(_ => Fixture.CreateDbContext())
            .AddScoped<EquityIssuerRepository>()
            .AddScoped<EquityListingRepository>()
            .AddScoped<EquityDirectorySourceRecordRepository>()
            .BuildServiceProvider();

    private EquityIssuerMergeManager Manager(params EquityReferenceRule[] rules) =>
        new(DbContext, new EquityIssuerRepository(DbContext), [new ExtraRules(rules)]);

    private sealed class ExtraRules(IEnumerable<EquityReferenceRule> rules) : IEquityReferenceRules
    {
        public IEnumerable<EquityReferenceRule> Rules { get; } = rules;
    }

    private async Task<EquityDirectorySourceRecord> Evidence()
    {
        var record = new EquityDirectorySourceRecord
        {
            Source = "gleif-isin",
            SourceRecordKey = "KYG017191142",
            PayloadHash = Guid.NewGuid().ToString("N"),
            PayloadJson = "{}",
        };
        DbContext.Add(record);
        await DbContext.SaveChangesAsync();
        return record;
    }

    private static EquityIssuerMergeRequest Request(
        Guid retired,
        Guid survivor,
        Guid evidence,
        bool execute = true
    ) =>
        new()
        {
            RetiredIssuerId = retired,
            SurvivingIssuerId = survivor,
            EvidenceKind = "gleif-related-isin",
            EvidenceSourceRecordIds = [evidence],
            MergedBy = "operator@example.org",
            WithdrawnFactPrefixes = ["VENUE-"],
            Execute = execute,
        };

    private static FinancialFact Fact(Guid issuer, FinancialConcept concept, string accession) =>
        new()
        {
            EquityIssuerId = issuer,
            FinancialConceptId = concept.Id,
            Unit = "CNY",
            Value = 1m,
            PeriodType = FactPeriodType.Duration,
            PeriodStart = new DateOnly(2025, 4, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            FiscalYear = 2026,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            Form = DocumentType.TwentyF,
            FiledDate = new DateOnly(2026, 6, 26),
            AccessionNumber = accession,
            DimensionsKey = "",
        };

    // A secondary venue listing imported as its own issuer, beside the SEC filer it duplicates.
    private async Task<(Guid Retired, EquityIssuer Survivor, Guid Listing)> SeedDuplicate(
        ServiceProvider services
    )
    {
        var listingId = await new EquityDirectoryIdentityImporter(
            services.GetRequiredService<IServiceScopeFactory>()
        ).ImportListing(HongKong());
        var survivor = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "BABA",
            Name: "Alibaba Group Holding Ltd",
            Cik: "0001577552",
            Cusip: "01609W102"
        );
        DbContext.Add(survivor);
        await DbContext.SaveChangesAsync();
        var retired = await DbContext
            .Set<EquityListing>()
            .Where(listing => listing.Id == listingId)
            .Select(listing => listing.Security.EquityIssuerId)
            .SingleAsync();
        return (retired, survivor, listingId);
    }

    [Fact]
    public async Task MergeFoldsTheHongKongDuplicateIntoTheSecIssuerAndKeepsEveryIdentity()
    {
        await using var services = Services();
        var (retired, survivor, listingId) = await SeedDuplicate(services);
        var concept = new FinancialConcept { Taxonomy = FactTaxonomy.IfrsFull, Tag = "Revenue" };
        var applied = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var dividend = new CashDividend
        {
            EquityIssuerId = retired,
            EquityListingId = listingId,
            Currency = "HKD",
            ExDate = new DateOnly(2026, 6, 12),
            AmountPerShare = 0.25m,
            Source = CashDividendSource.Yahoo,
        };
        var split = new StockSplit
        {
            EquityIssuerId = retired,
            EquityListingId = listingId,
            PriceSeriesTicker = "09988",
            EffectiveDate = new DateOnly(2019, 7, 30),
            Numerator = 8m,
            Denominator = 1m,
            Source = StockSplitSource.Yahoo,
            PriceAdjustmentAppliedTime = applied,
            ShareCountEvidence = StockSplitShareCountEvidence.Confirmed,
            ShareCountEvidenceAsOf = new DateOnly(2019, 9, 30),
            ShareCountEvidenceTime = applied,
        };
        var retiredRow = await DbContext.Set<EquityIssuer>().SingleAsync(row => row.Id == retired);
        retiredRow.LegalEntityIdentifier = "5493001NTNQJDH60PM02";
        retiredRow.Website = "https://www.example.org/";
        DbContext.Add(concept);
        DbContext.AddRange(
            dividend,
            split,
            new CompanyFilingSyncState { EquityIssuerId = retired, LastSyncedAt = applied },
            new CompanyFilingSyncState
            {
                EquityIssuerId = survivor.Id,
                LastSyncedAt = applied.AddDays(1),
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.Add(Fact(retired, concept, "VENUE-2026-0001"));
        await DbContext.SaveChangesAsync();
        var evidence = await Evidence();

        var report = await Manager().Merge(Request(retired, survivor.Id, evidence.Id));

        report.Refusals.Should().BeEmpty();
        report.Executed.Should().BeTrue();
        report.WithdrawnFacts.Should().Be(1);
        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<EquityIssuer>().AnyAsync(row => row.Id == retired)).Should().BeFalse();
        var listing = await DbContext
            .Set<EquityListing>()
            .Include(row => row.Security)
            .SingleAsync(row => row.Id == listingId);
        listing.Security.EquityIssuerId.Should().Be(survivor.Id);
        var movedDividend = await DbContext
            .Set<CashDividend>()
            .SingleAsync(row => row.Id == dividend.Id);
        movedDividend.EquityIssuerId.Should().Be(survivor.Id);
        movedDividend.EquityListingId.Should().Be(listingId);
        var movedSplit = await DbContext.Set<StockSplit>().SingleAsync(row => row.Id == split.Id);
        movedSplit.EquityIssuerId.Should().Be(survivor.Id);
        movedSplit.PriceAdjustmentAppliedTime.Should().Be(applied);
        // The share-count verdict was judged against the retired issuer's counts, so it is judged again.
        movedSplit.ShareCountEvidence.Should().BeNull();
        movedSplit.ShareCountEvidenceAsOf.Should().BeNull();
        movedSplit.ShareCountEvidenceTime.Should().BeNull();
        (await DbContext.Set<CompanyFilingSyncState>().SingleAsync())
            .EquityIssuerId.Should()
            .Be(survivor.Id);
        (await DbContext.Set<FinancialFact>().AnyAsync()).Should().BeFalse();
        (
            await DbContext
                .Set<EquityIssuerSourceIdentifier>()
                .Where(row => row.Identifier == "security:09988")
                .Select(row => row.EquityIssuerId)
                .SingleAsync()
        )
            .Should()
            .Be(survivor.Id);
        var merged = await DbContext
            .Set<EquityIssuer>()
            .Include(row => row.Presentation)
            .SingleAsync(row => row.Id == survivor.Id);
        merged.Name.Should().Be("Alibaba Group Holding Ltd");
        merged.NameAliases.Should().Contain("BABA-W");
        merged.LegalEntityIdentifier.Should().Be("5493001NTNQJDH60PM02");
        merged.Website.Should().Be("https://www.example.org/");
        merged.Presentation.EquityListingId.Should().Be(survivor.Presentation.EquityListingId);
        var audit = await DbContext.Set<EquityIssuerMerge>().SingleAsync();
        audit.RetiredIssuerId.Should().Be(retired);
        audit.SurvivingIssuerId.Should().Be(survivor.Id);
        audit.EvidenceSourceRecordIds.Should().Equal(evidence.Id);
        audit.CustomerReferencesRepointedAt.Should().BeNull();
        System
            .Text.Json.JsonSerializer.Deserialize<EquityIssuerMergeReport>(audit.ReportJson)
            .Executed.Should()
            .BeTrue();

        // The next directory cycle resolves the same row to the survivor and leaves its SEC name alone.
        (
            await new EquityDirectoryIdentityImporter(
                services.GetRequiredService<IServiceScopeFactory>()
            ).ImportListing(HongKong())
        )
            .Should()
            .Be(listingId);
        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<EquityIssuer>().SingleAsync(row => row.Id == survivor.Id))
            .Name.Should()
            .Be("Alibaba Group Holding Ltd");
        (await DbContext.Set<EquityIssuer>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PreviewReportsTheMergeAndWritesNothing()
    {
        await using var services = Services();
        var (retired, survivor, _) = await SeedDuplicate(services);
        var concept = new FinancialConcept { Taxonomy = FactTaxonomy.IfrsFull, Tag = "Revenue" };
        DbContext.Add(concept);
        await DbContext.SaveChangesAsync();
        DbContext.Add(Fact(retired, concept, "VENUE-2026-0001"));
        await DbContext.SaveChangesAsync();
        var evidence = await Evidence();

        var report = await Manager()
            .Merge(Request(retired, survivor.Id, evidence.Id, execute: false));

        report.IsClean.Should().BeTrue();
        report.Executed.Should().BeFalse();
        report.WithdrawnFacts.Should().Be(1);
        report
            .Changes.Should()
            .Contain(change => change.Table == "EquitySecurity" && change.Moved == 1);
        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<EquityIssuer>().AnyAsync(row => row.Id == retired)).Should().BeTrue();
        (await DbContext.Set<FinancialFact>().CountAsync()).Should().Be(1);
        (await DbContext.Set<EquityIssuerMerge>().AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData("sec-filer")]
    [InlineData("governing-fact")]
    [InlineData("duplicate-registration")]
    [InlineData("no-evidence")]
    [InlineData("different-lei")]
    [InlineData("secondary-cik")]
    [InlineData("self-merge")]
    [InlineData("blank-prefix")]
    [InlineData("issuer-level-dividend")]
    public async Task AnUnsafeMergeIsRefusedAndRolledBack(string defect)
    {
        await using var services = Services();
        var (retired, survivor, listingId) = await SeedDuplicate(services);
        var concept = new FinancialConcept { Taxonomy = FactTaxonomy.IfrsFull, Tag = "Revenue" };
        DbContext.Add(concept);
        await DbContext.SaveChangesAsync();
        DbContext.Add(
            Fact(
                retired,
                concept,
                defect is "governing-fact" or "blank-prefix"
                    ? "FILING-2026-0001"
                    : "VENUE-2026-0001"
            )
        );
        if (defect == "duplicate-registration")
        {
            foreach (var issuer in new[] { retired, survivor.Id })
                DbContext.Add(
                    new IssuerSecurityRegistration
                    {
                        EquityIssuerId = issuer,
                        TradingSymbol = "BABA",
                        Title = "American depositary shares",
                        FiledDate = new DateOnly(2026, 6, 26),
                    }
                );
        }
        if (defect == "issuer-level-dividend")
            DbContext.Add(
                new CashDividend
                {
                    EquityIssuerId = retired,
                    Currency = "HKD",
                    ExDate = new DateOnly(2026, 6, 12),
                    AmountPerShare = 0.25m,
                    Source = CashDividendSource.Yahoo,
                }
            );
        await DbContext.SaveChangesAsync();
        if (defect is "sec-filer" or "different-lei" or "secondary-cik")
        {
            var row = await DbContext
                .Set<EquityIssuer>()
                .SingleAsync(issuer => issuer.Id == retired);
            if (defect == "sec-filer")
                row.Cik = "0000000001";
            else if (defect == "secondary-cik")
                row.SecondaryCiks = ["0000000002"];
            else
            {
                row.LegalEntityIdentifier = "5493001NTNQJDH60PM02";
                (
                    await DbContext
                        .Set<EquityIssuer>()
                        .SingleAsync(issuer => issuer.Id == survivor.Id)
                ).LegalEntityIdentifier = "529900T8BM49AURSDO55";
            }
            await DbContext.SaveChangesAsync();
        }
        var evidence = await Evidence();
        var request = Request(retired, defect == "self-merge" ? retired : survivor.Id, evidence.Id);
        if (defect == "blank-prefix")
            request = new EquityIssuerMergeRequest
            {
                RetiredIssuerId = retired,
                SurvivingIssuerId = survivor.Id,
                EvidenceKind = request.EvidenceKind,
                EvidenceSourceRecordIds = request.EvidenceSourceRecordIds,
                MergedBy = request.MergedBy,
                WithdrawnFactPrefixes = [""],
                Execute = true,
            };
        if (defect == "no-evidence")
            request = new EquityIssuerMergeRequest
            {
                RetiredIssuerId = retired,
                SurvivingIssuerId = survivor.Id,
                EvidenceKind = request.EvidenceKind,
                EvidenceSourceRecordIds = [Guid.NewGuid()],
                MergedBy = request.MergedBy,
                WithdrawnFactPrefixes = request.WithdrawnFactPrefixes,
                Execute = true,
            };

        var report = await Manager().Merge(request);

        report.Refusals.Should().NotBeEmpty();
        if (defect == "self-merge")
            report.Refusals.Should().Contain(reason => reason.Contains("into itself"));
        report.Executed.Should().BeFalse();
        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<EquityIssuer>().AnyAsync(row => row.Id == retired)).Should().BeTrue();
        (await DbContext.Set<FinancialFact>().CountAsync(row => row.EquityIssuerId == retired))
            .Should()
            .Be(1);
        (
            await DbContext
                .Set<EquityListing>()
                .Where(row => row.Id == listingId)
                .Select(row => row.Security.EquityIssuerId)
                .SingleAsync()
        )
            .Should()
            .Be(retired);
        (await DbContext.Set<EquityIssuerMerge>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task AMergeNeverDiscardsTheCallersPendingChanges()
    {
        DbContext.Add(
            Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "PEND", Name: "Pending")
        );

        var merge = () => Manager().Merge(Request(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        await merge.Should().ThrowAsync<InvalidOperationException>();
        DbContext.ChangeTracker.HasChanges().Should().BeTrue();
    }

    [Fact]
    public async Task ChainedMergesPointEveryAuditRowAtTheLastSurvivor()
    {
        var first = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "AAA", Name: "First");
        var second = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "BBB", Name: "Second");
        var third = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "CCC", Name: "Third");
        DbContext.AddRange(first, second, third);
        await DbContext.SaveChangesAsync();
        var evidence = await Evidence();

        (await Manager().Merge(Request(first.Id, second.Id, evidence.Id)))
            .Refusals.Should()
            .BeEmpty();
        (await Manager().Merge(Request(second.Id, third.Id, evidence.Id)))
            .Refusals.Should()
            .BeEmpty();

        DbContext.ChangeTracker.Clear();
        (
            await DbContext
                .Set<EquityIssuerMerge>()
                .Select(row => row.SurvivingIssuerId)
                .ToListAsync()
        )
            .Should()
            .Equal(third.Id, third.Id);
        (await DbContext.Set<EquityIssuer>().SingleAsync())
            .NameAliases.Should()
            .Contain(["First", "Second"]);
    }

    [Theory]
    [InlineData("keep-survivor")]
    [InlineData("no-rule")]
    [InlineData("no-dependent-rekey")]
    [InlineData("unruled-column")]
    public async Task DuplicateRowsFollowTheSurvivorOnlyUnderAKeepSurvivorRule(string variant)
    {
        var retired = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "OLD", Name: "Old");
        var survivor = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "NEW", Name: "New");
        DbContext.AddRange(retired, survivor);
        await DbContext.SaveChangesAsync();
        var evidence = await Evidence();
        Guid duplicate = Guid.NewGuid(),
            kept = Guid.NewGuid(),
            unique = Guid.NewGuid();
        Guid child = Guid.NewGuid(),
            retiredSoft = Guid.NewGuid(),
            keptSoft = Guid.NewGuid();
        await DbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE "MergeProbeParent" ("Id" uuid PRIMARY KEY,
                "EquityIssuerId" uuid NOT NULL REFERENCES "EquityIssuer" ("Id"), "Url" text NOT NULL,
                UNIQUE ("EquityIssuerId", "Url"));
            CREATE TABLE "MergeProbeChild" ("Id" uuid PRIMARY KEY,
                "ParentId" uuid NOT NULL REFERENCES "MergeProbeParent" ("Id"));
            CREATE TABLE "MergeProbeSoft" ("Id" uuid PRIMARY KEY, "ParentKey" uuid NOT NULL UNIQUE);
            CREATE TABLE "MergeProbeLoose" ("Id" uuid PRIMARY KEY, "EquityIssuerId" uuid NOT NULL);
            """
        );
        try
        {
            await DbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "MergeProbeParent" VALUES ({duplicate}, {retired.Id}, 'a'), ({kept}, {survivor.Id}, 'a'),
                    ({unique}, {retired.Id}, 'b');
                INSERT INTO "MergeProbeChild" VALUES ({child}, {duplicate});
                INSERT INTO "MergeProbeSoft" VALUES ({retiredSoft}, {duplicate}), ({keptSoft}, {kept});
                """
            );
            if (variant == "unruled-column")
                await DbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO \"MergeProbeLoose\" VALUES ({Guid.NewGuid()}, {retired.Id})"
                );
            var rules = variant switch
            {
                "no-rule" => [],
                _ => new[]
                {
                    new EquityReferenceRule("MergeProbeParent")
                    {
                        OnCollision = EquityReferenceCollision.KeepSurvivor,
                        RekeyDependents = variant != "no-dependent-rekey",
                        SoftReferences = [new EquityColumnReference("MergeProbeSoft", "ParentKey")],
                    },
                    new EquityReferenceRule("MergeProbeSoft")
                    {
                        OnCollision = EquityReferenceCollision.KeepSurvivor,
                    },
                },
            };

            var report = await Manager(rules).Merge(Request(retired.Id, survivor.Id, evidence.Id));

            var parents = await DbContext
                .Database.SqlQueryRaw<Guid>(
                    "SELECT \"EquityIssuerId\" AS \"Value\" FROM \"MergeProbeParent\" ORDER BY \"Url\", \"EquityIssuerId\""
                )
                .ToListAsync();
            var childParent = await DbContext
                .Database.SqlQueryRaw<Guid>(
                    "SELECT \"ParentId\" AS \"Value\" FROM \"MergeProbeChild\""
                )
                .SingleAsync();
            var softRows = await DbContext
                .Database.SqlQueryRaw<Guid>("SELECT \"Id\" AS \"Value\" FROM \"MergeProbeSoft\"")
                .ToListAsync();
            if (variant == "keep-survivor")
            {
                report.Refusals.Should().BeEmpty();
                report
                    .Remaps.Should()
                    .Equal(
                        new EquityKeyRemap("MergeProbeSoft", retiredSoft, keptSoft),
                        new EquityKeyRemap("MergeProbeParent", duplicate, kept)
                    );
                parents.Should().Equal(survivor.Id, survivor.Id);
                childParent.Should().Be(kept);
                softRows.Should().Equal(keptSoft);
            }
            else
            {
                report
                    .Refusals.Should()
                    .ContainSingle()
                    .Which.Should()
                    .Contain(variant == "unruled-column" ? "MergeProbeLoose" : "MergeProbeParent");
                report.Executed.Should().BeFalse();
                parents.Should().HaveCount(3);
                childParent.Should().Be(duplicate);
                softRows.Should().HaveCount(2);
            }
        }
        finally
        {
            await DbContext.Database.ExecuteSqlRawAsync(
                """
                DROP TABLE "MergeProbeLoose"; DROP TABLE "MergeProbeSoft";
                DROP TABLE "MergeProbeChild"; DROP TABLE "MergeProbeParent";
                """
            );
        }
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("nulls-not-distinct")]
    [InlineData("expression")]
    public async Task UniqueIndexPredicatesAndExpressionsDecideWhatCollides(string variant)
    {
        var retired = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "OLD", Name: "Old");
        var survivor = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "NEW", Name: "New");
        DbContext.AddRange(retired, survivor);
        await DbContext.SaveChangesAsync();
        var evidence = await Evidence();
        var index = variant switch
        {
            "partial" =>
                "CREATE UNIQUE INDEX \"IX_MergeProbeScoped\" ON \"MergeProbeScoped\" (\"EquityIssuerId\", \"Code\") WHERE \"Active\"",
            "nulls-not-distinct" =>
                "CREATE UNIQUE INDEX \"IX_MergeProbeScoped\" ON \"MergeProbeScoped\" (\"EquityIssuerId\", \"Code\") NULLS NOT DISTINCT",
            _ =>
                "CREATE UNIQUE INDEX \"IX_MergeProbeScoped\" ON \"MergeProbeScoped\" (\"EquityIssuerId\", lower(\"Code\"))",
        };
        await DbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE "MergeProbeScoped" ("Id" uuid PRIMARY KEY,
                "EquityIssuerId" uuid NOT NULL REFERENCES "EquityIssuer" ("Id"), "Code" text, "Active" boolean NOT NULL);
            """ + index
        );
        try
        {
            // Partial: only the survivor's row is inside the predicate, so the retired row is no duplicate.
            var code = variant == "nulls-not-distinct" ? null : "a";
            await DbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "MergeProbeScoped" VALUES ({Guid.NewGuid()}, {retired.Id}, {code}, false),
                    ({Guid.NewGuid()}, {survivor.Id}, {code}, true);
                """
            );

            var report = await Manager().Merge(Request(retired.Id, survivor.Id, evidence.Id));

            var owners = await DbContext
                .Database.SqlQueryRaw<Guid>(
                    "SELECT \"EquityIssuerId\" AS \"Value\" FROM \"MergeProbeScoped\""
                )
                .ToListAsync();
            if (variant == "partial")
            {
                report.Refusals.Should().BeEmpty();
                owners.Should().Equal(survivor.Id, survivor.Id);
            }
            else
            {
                report
                    .Refusals.Should()
                    .ContainSingle()
                    .Which.Should()
                    .Contain(
                        variant == "expression"
                            ? "uses an expression"
                            : "duplicate the survivor's on a unique key"
                    );
                owners.Should().BeEquivalentTo([retired.Id, survivor.Id]);
            }
        }
        finally
        {
            await DbContext.Database.ExecuteSqlRawAsync("DROP TABLE \"MergeProbeScoped\"");
        }
    }
}
