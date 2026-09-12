using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class EarlyDirectoryPreservationTests(ParadeDbFixture fixture)
{
    [Fact]
    public async Task FirstExpansionCapturesEveryDirectoryFieldAndProtectsFactsUntilTheirOwnerMoves()
    {
        await using var database = await IsolatedMigrationDatabase.Create(
            fixture,
            "20260909160010_CoverNportHolderCount"
        );
        var context = database.Context;
        var stock = new CommonStock
        {
            Ticker = "PRESERVE",
            Name = "Original € — ação",
            Cik = "0000000093",
            Description = "Original filing text",
            SecondaryTickers = ["PRESERVE-B", "PRESERVE-B"],
            SecondaryCiks = ["123", "123"],
            HistoricalCusipBackfillCandidates = ["037833100", "037833100"],
            HistoricalCusipBackfillAmbiguous = true,
            MarketCapitalization = 12345.678901234567,
            SharesOutStanding = 98765432101234,
            HistoricalCusipBackfillRequestedAt = new DateTime(
                2020,
                1,
                2,
                3,
                4,
                5,
                DateTimeKind.Utc
            ),
        };
        var concept = new FinancialConcept
        {
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "Revenues",
            Label = "Original revenue",
        };
        context.AddRange(stock, concept);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "FinancialFact" ("Id", "CommonStockId", "FinancialConceptId", "DocumentId", "Unit", "PeriodType",
                "PeriodStart", "PeriodEnd", "Value", "FiscalYear", "FiscalPeriod", "Form", "FiledDate", "AccessionNumber", "Frame", "DimensionsKey", "CreationTime")
            VALUES ({Guid.NewGuid()}, {stock.Id}, {concept.Id}, NULL, 'EUR', 1,
                DATE '2025-01-01', DATE '2025-12-31', -123456789012345.123456789, 2025, 0,
                'TwentyF', DATE '2026-03-15', 'original', NULL, 'dimensional-source-key', TIMESTAMPTZ '2026-03-16 01:02:03.123456Z');
            CREATE TABLE "OriginalFacts" AS SELECT to_jsonb(fact) AS payload FROM "FinancialFact" fact;
            """
        );
        var original = await context
            .Database.SqlQuery<string>(
                $"""SELECT to_jsonb(source)::text AS "Value" FROM "CommonStock" source WHERE "Id" = {stock.Id}"""
            )
            .SingleAsync();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260911164403_AddEquityIdentityFoundation");
        var evidence = await context
            .Set<EquityDirectorySourceRecord>()
            .AsNoTracking()
            .SingleAsync();
        evidence.PayloadJson.Should().Be(original);
        evidence.SourceRecordKey.Should().Be(stock.Id.ToString());
        Func<Task> retire = () =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"""DELETE FROM "CommonStock" WHERE "Id" = {stock.Id}"""
            );
        (await retire.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.ForeignKeyViolation);
        await AssertFacts(context);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "CommonStock" SET "Name" = 'Updated before long backfills' WHERE "Id" = {stock.Id}"""
        );
        var updated = await context
            .Database.SqlQuery<string>(
                $"""SELECT to_jsonb(source)::text AS "Value" FROM "CommonStock" source WHERE "Id" = {stock.Id}"""
            )
            .SingleAsync();
        (
            await context
                .Set<EquityDirectorySourceRecord>()
                .Select(row => row.PayloadJson)
                .ToListAsync()
        )
            .Should()
            .BeEquivalentTo(original, updated);

        await migrator.MigrateAsync("20260911232125_RetargetFinancialFactsToIssuers");
        await retire();
        await AssertFacts(context);
        (
            await context
                .Database.SqlQuery<Guid>(
                    $"""SELECT "Id" AS "Value" FROM "EquityIssuer" WHERE "Id" = {stock.Id}"""
                )
                .SingleAsync()
        )
            .Should()
            .Be(stock.Id);
        await context.Database.MigrateAsync();
        await AssertFacts(context);
        var retained = await context
            .Set<EquityDirectorySourceRecord>()
            .AsNoTracking()
            .ToListAsync();
        retained.Select(row => row.PayloadJson).Should().BeEquivalentTo(original, updated);
        retained.Single(row => row.PayloadJson == original).Id.Should().Be(evidence.Id);
    }

    [Theory]
    [InlineData("CASCADE")]
    [InlineData("SET NULL")]
    [InlineData("SET DEFAULT")]
    public async Task EarlyGuardRefusesDestructiveForeignKeysAndAllowsRetirementAfterRetarget(
        string deleteAction
    )
    {
        await using var database = await IsolatedMigrationDatabase.Create(
            fixture,
            "20260911164403_AddEquityIdentityFoundation"
        );
        var context = database.Context;
        var stock = new CommonStock { Ticker = "OWNED", Name = "Original owner" };
        context.Add(stock);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync(
            $"""
            CREATE TABLE "HistoricalOwnership" ("Id" integer PRIMARY KEY, "OwnerId" uuid, "OriginalFact" text,
                CONSTRAINT original_owner FOREIGN KEY ("OwnerId") REFERENCES "CommonStock"("Id") ON DELETE {deleteAction});
            """
        );
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "HistoricalOwnership" VALUES (1, {stock.Id}, 'Source association — retained');
            INSERT INTO "EquityIssuer" ("Id", "Name", "CommonStockId", "IdentitySourceUrl") VALUES ({stock.Id}, 'Native owner', {stock.Id}, '');
            """
        );
        var before = await context
            .Database.SqlQueryRaw<string>(
                """SELECT to_jsonb(history)::text AS "Value" FROM "HistoricalOwnership" history"""
            )
            .SingleAsync();
        Func<Task> retire = () =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"""DELETE FROM "CommonStock" WHERE "Id" = {stock.Id}"""
            );
        (await retire.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.ForeignKeyViolation);
        (
            await context
                .Database.SqlQueryRaw<string>(
                    """SELECT to_jsonb(history)::text AS "Value" FROM "HistoricalOwnership" history"""
                )
                .SingleAsync()
        )
            .Should()
            .Be(before);
        await context.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "HistoricalOwnership" DROP CONSTRAINT original_owner;
            ALTER TABLE "HistoricalOwnership" ADD CONSTRAINT native_owner FOREIGN KEY ("OwnerId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT;
            """
        );
        await retire();
        (
            await context
                .Database.SqlQueryRaw<string>(
                    """SELECT to_jsonb(history)::text AS "Value" FROM "HistoricalOwnership" history"""
                )
                .SingleAsync()
        )
            .Should()
            .Be(before);
    }

    private static async Task AssertFacts(Equibles.Data.EquiblesFinancialDbContext context)
    {
        (
            await context
                .Database.SqlQueryRaw<long>(
                    """
                    SELECT count(*) AS "Value" FROM (
                        (SELECT payload FROM "OriginalFacts" EXCEPT ALL SELECT to_jsonb(fact) - 'EquityIssuerId' FROM "FinancialFact" fact)
                        UNION ALL
                        (SELECT to_jsonb(fact) - 'EquityIssuerId' FROM "FinancialFact" fact EXCEPT ALL SELECT payload FROM "OriginalFacts")
                    ) differences
                    """
                )
                .SingleAsync()
        ).Should().Be(0);
    }
}
