using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.IntegrationTests.CorporateActions;

// The finite correction behind SplitEvents.CrossSourceSameEventWindowDays: a lower source's
// stale date of a sizable split merges into the higher source's row weeks away, and nothing
// else is touched.
[Collection(ParadeDbCollection.Name)]
public class CollapseCrossSourceSplitCapturesMigrationTests(ParadeDbFixture fixture)
{
    private const string BeforeMigration = "20261106174442_CanadianReleaseCalendar";
    private const string MigrationFile =
        "src/Equibles.Migrations/Migrations/20261107000905_CollapseCrossSourceSplitCaptures.cs";

    [Fact]
    public async Task Up_DropsTheLowerSourcesStaleDateAndLeavesEveryOtherRowAlone()
    {
        await using var database = await IsolatedMigrationDatabase.Create(fixture, BeforeMigration);
        var context = database.Context;
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "RUSHA",
            SecondaryTickers: ["RUSHB"]
        );
        context.Add(issuer);
        await context.SaveChangesAsync();
        var primary = issuer.Presentation.EquityListingId;
        var sibling = issuer
            .Securities.SelectMany(security => security.Listings)
            .Single(listing => listing.Ticker == "RUSHB")
            .Id;
        StockSplit Row(
            Guid? listingId,
            string ticker,
            DateOnly date,
            StockSplitSource source,
            decimal numerator = 3m,
            decimal denominator = 2m
        ) =>
            new()
            {
                EquityIssuerId = issuer.Id,
                EquityListingId = listingId,
                PriceSeriesTicker = ticker,
                EffectiveDate = date,
                Numerator = numerator,
                Denominator = denominator,
                Source = source,
                PriceAdjustmentAppliedTime = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
            };
        var rows = new Dictionary<string, StockSplit>
        {
            // The chart's stale date three weeks before the feed's executed date.
            ["charted"] = Row(primary, "RUSHA", new DateOnly(2026, 8, 11), StockSplitSource.Yahoo),
            ["executed"] = Row(
                primary,
                "RUSHA",
                new DateOnly(2026, 9, 1),
                StockSplitSource.External
            ),
            // Precedence beats date in the other direction too.
            ["feedEarly"] = Row(
                primary,
                "RUSHA",
                new DateOnly(2025, 3, 3),
                StockSplitSource.External
            ),
            ["chartLate"] = Row(
                primary,
                "RUSHA",
                new DateOnly(2025, 3, 24),
                StockSplitSource.Yahoo
            ),
            // Not the same event: one source repeating, a near-unity ratio, beyond the window,
            // a sibling listing, no listing.
            ["repeat1"] = Row(
                primary,
                "RUSHA",
                new DateOnly(2024, 5, 6),
                StockSplitSource.External
            ),
            ["repeat2"] = Row(
                primary,
                "RUSHA",
                new DateOnly(2024, 5, 27),
                StockSplitSource.External
            ),
            ["monthly1"] = Row(
                primary,
                "RUSHA",
                new DateOnly(2023, 10, 2),
                StockSplitSource.Yahoo,
                numerator: 101m,
                denominator: 100m
            ),
            ["monthly2"] = Row(
                primary,
                "RUSHA",
                new DateOnly(2023, 11, 1),
                StockSplitSource.External,
                numerator: 101m,
                denominator: 100m
            ),
            ["far1"] = Row(primary, "RUSHA", new DateOnly(2022, 1, 3), StockSplitSource.Yahoo),
            ["far2"] = Row(primary, "RUSHA", new DateOnly(2022, 2, 18), StockSplitSource.External),
            ["siblingCharted"] = Row(
                sibling,
                "RUSHB",
                new DateOnly(2026, 8, 11),
                StockSplitSource.Yahoo
            ),
            ["legacy"] = Row(null, "RUSHA", new DateOnly(2026, 8, 11), StockSplitSource.Yahoo),
            // Rows with a non-positive arm describe no ratio: the gate must neither divide by
            // them nor merge them, whatever the planner evaluates first.
            ["zeroDenominator"] = Row(
                primary,
                "RUSHA",
                new DateOnly(2021, 6, 1),
                StockSplitSource.Yahoo,
                numerator: 3m,
                denominator: 0m
            ),
            ["zeroNumerator"] = Row(
                primary,
                "RUSHA",
                new DateOnly(2020, 6, 1),
                StockSplitSource.Yahoo,
                numerator: 0m,
                denominator: 2m
            ),
            ["zeroNumeratorFeed"] = Row(
                primary,
                "RUSHA",
                new DateOnly(2020, 6, 11),
                StockSplitSource.External,
                numerator: 0m,
                denominator: 2m
            ),
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
            "feedEarly",
            "repeat1",
            "repeat2",
            "monthly1",
            "monthly2",
            "far1",
            "far2",
            "siblingCharted",
            "legacy",
            "zeroDenominator",
            "zeroNumerator",
            "zeroNumeratorFeed",
        };
        survivors.Should().BeEquivalentTo(expected.Select(key => rows[key].Id));
        var kept = await context
            .Set<StockSplit>()
            .AsNoTracking()
            .SingleAsync(row => row.Id == rows["executed"].Id);
        kept.PriceAdjustmentAppliedTime.Should()
            .NotBeNull("the survivor's closes already carry this split once");
    }

    [Fact]
    public void Up_CarriesTheWritersWindowAndRatioGate()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot(), MigrationFile));

        migration
            .Should()
            .Contain(
                $"abs(survivor.\"EffectiveDate\" - duplicate.\"EffectiveDate\") <= {SplitEvents.CrossSourceSameEventWindowDays}"
            )
            .And.Contain(
                $"abs(duplicate.\"Numerator\" / duplicate.\"Denominator\" - 1) END >= {SplitEvents.MinimumCrossSourceRatioShift.ToString(CultureInfo.InvariantCulture)}"
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
