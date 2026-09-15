using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.IntegrationTests.CorporateActions;

// The finite correction behind StockSplitCaptureManager.SameEventWindowDays: one stored row per
// event, chosen the way the writer chooses it, and nothing else touched.
[Collection(ParadeDbCollection.Name)]
public class CollapseDuplicateSplitCapturesMigrationTests(ParadeDbFixture fixture)
{
    private const string BeforeMigration = "20260915014158_AddHoldingRepairScanIndexes";
    private const string MigrationFile =
        "src/Equibles.Migrations/Migrations/20260915120253_CollapseDuplicateSplitCaptures.cs";

    [Fact]
    public async Task Up_KeepsOneRowPerEventAndLeavesEveryOtherRowAlone()
    {
        await using var database = await IsolatedMigrationDatabase.Create(fixture, BeforeMigration);
        var context = database.Context;
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "COLL",
            SecondaryTickers: ["COLL-B"]
        );
        context.Add(issuer);
        await context.SaveChangesAsync();
        var primary = issuer.Presentation.EquityListingId;
        var sibling = issuer
            .Securities.SelectMany(security => security.Listings)
            .Single(listing => listing.Ticker == "COLL-B")
            .Id;
        StockSplit Row(
            Guid? listingId,
            string ticker,
            DateOnly date,
            StockSplitSource source = StockSplitSource.Yahoo,
            decimal denominator = 50m
        ) =>
            new()
            {
                EquityIssuerId = issuer.Id,
                EquityListingId = listingId,
                PriceSeriesTicker = ticker,
                EffectiveDate = date,
                Numerator = 1m,
                Denominator = denominator,
                Source = source,
                PriceAdjustmentAppliedTime = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            };
        var rows = new Dictionary<string, StockSplit>
        {
            // Same source: the later date is the executed one.
            ["announced"] = Row(primary, "COLL", new DateOnly(2026, 1, 1)),
            ["executed"] = Row(primary, "COLL", new DateOnly(2026, 1, 2)),
            // A three-row chain collapses transitively onto its last date.
            ["chain1"] = Row(primary, "COLL", new DateOnly(2026, 3, 20)),
            ["chain2"] = Row(primary, "COLL", new DateOnly(2026, 3, 24)),
            ["chain3"] = Row(primary, "COLL", new DateOnly(2026, 3, 25)),
            // Precedence beats date in both directions.
            ["yahooEarly"] = Row(primary, "COLL", new DateOnly(2026, 8, 21)),
            ["externalLate"] = Row(
                primary,
                "COLL",
                new DateOnly(2026, 8, 24),
                StockSplitSource.External
            ),
            ["yahooLate"] = Row(primary, "COLL", new DateOnly(2026, 6, 10)),
            ["externalEarly"] = Row(
                primary,
                "COLL",
                new DateOnly(2026, 6, 3),
                StockSplitSource.External
            ),
            // Not the same event: a month apart, a different ratio, a sibling listing, no listing.
            ["monthly1"] = Row(primary, "COLL", new DateOnly(2025, 10, 1)),
            ["monthly2"] = Row(primary, "COLL", new DateOnly(2025, 11, 6)),
            ["ratio50"] = Row(primary, "COLL", new DateOnly(2025, 5, 5)),
            ["ratio10"] = Row(primary, "COLL", new DateOnly(2025, 5, 6), denominator: 10m),
            ["siblingSameDay"] = Row(sibling, "COLL-B", new DateOnly(2026, 1, 2)),
            ["legacy1"] = Row(null, "COLL", new DateOnly(2024, 2, 1)),
            ["legacy2"] = Row(null, "COLL", new DateOnly(2024, 2, 2)),
        };
        context.AddRange(rows.Values);
        await context.SaveChangesAsync();

        await context.GetService<IMigrator>().MigrateAsync();

        var survivors = await context
            .Set<StockSplit>()
            .AsNoTracking()
            .Select(row => row.Id)
            .ToListAsync();
        var expected = new[]
        {
            "executed",
            "chain3",
            "externalLate",
            "externalEarly",
            "monthly1",
            "monthly2",
            "ratio50",
            "ratio10",
            "siblingSameDay",
            "legacy1",
            "legacy2",
        };
        survivors.Should().BeEquivalentTo(expected.Select(key => rows[key].Id));
        var kept = await context
            .Set<StockSplit>()
            .AsNoTracking()
            .SingleAsync(row => row.Id == rows["externalLate"].Id);
        kept.PriceAdjustmentAppliedTime.Should()
            .NotBeNull("the survivor's own marker is untouched");
    }

    [Fact]
    public void Up_CarriesTheWritersWindow()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot(), MigrationFile));

        migration
            .Should()
            .Contain(
                $"abs(survivor.\"EffectiveDate\" - duplicate.\"EffectiveDate\") <= {StockSplitCaptureManager.SameEventWindowDays}"
            );
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Equibles.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
