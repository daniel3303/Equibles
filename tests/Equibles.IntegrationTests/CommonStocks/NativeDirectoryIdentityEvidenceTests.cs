using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class NativeDirectoryIdentityEvidenceTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    private static readonly string[] Tables =
    [
        "CommonStockCusipAlias",
        "CommonStockListedCusip",
        "CommonStockTickerAlias",
        "CommonStockDelistedListing",
    ];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DirectoryEvidence_PreservesEveryField_IndependentOfLegacyOwner(bool migrate)
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        EquityIssuer issuer;
        if (migrate)
        {
            var stock = new CommonStock { Ticker = "PRESENT", Name = "Original issuer" };
            DbContext.Add(stock);
            await DbContext.SaveChangesAsync();
            issuer = await DbContext.Set<EquityIssuer>().SingleAsync(row => row.Id == stock.Id);
        }
        else
        {
            issuer = new EquityIssuer { Name = "Native issuer" };
            DbContext.Add(issuer);
        }
        var time = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        DbContext.AddRange(
            new EquityIssuerCusipAlias
            {
                Issuer = issuer,
                Cusip = "123456789",
                CreationTime = time,
            },
            new EquityListingCusipEvidence
            {
                Issuer = issuer,
                Cusip = "987654321",
                ListedTicker = "SECONDARY",
                CreationTime = time,
            },
            new EquityIssuerTickerAlias
            {
                Issuer = issuer,
                Ticker = "FORMER",
                CreationTime = time,
            },
            new EquityListingRetirementEvidence
            {
                Issuer = issuer,
                ListedTicker = "RETIRED",
                Cusip = "456789123",
                DelistedOn = new DateOnly(2020, 1, 1),
                HistoricalPriceBackfillAttemptedAt = time,
                HistoricalCusipBackfillRequestedAt = time.AddDays(1),
                HistoricalCusipBackfillCandidates = ["456789123", "456789124", "456789123"],
                HistoricalCusipBackfillCandidateOn = new DateOnly(2019, 12, 31),
                HistoricalCusipBackfillAmbiguous = true,
                HistoricalCusipBackfillSweepStartedAt = time.AddDays(2),
            }
        );
        await DbContext.SaveChangesAsync();
        if (migrate)
            foreach (var table in Tables)
                await DbContext.Database.ExecuteSqlRawAsync(
                    $"""
                    ALTER TABLE "{table}" DROP CONSTRAINT "FK_{table}_EquityIssuer_CommonStockId";
                    ALTER TABLE "{table}" ADD CONSTRAINT "FK_{table}_CommonStock_CommonStockId"
                    FOREIGN KEY ("CommonStockId") REFERENCES "CommonStock"("Id") ON DELETE CASCADE;
                    """
                );
        var before = await Snapshot();
        if (migrate)
        {
            foreach (
                var command in DbContext
                    .GetService<IMigrationsSqlGenerator>()
                    .Generate(new RetargetDirectoryIdentityEvidence().UpOperations)
            )
                await DbContext.Database.ExecuteSqlRawAsync(command.CommandText);
            (await Snapshot()).Should().Be(before);
            await DbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "EquityIssuer" SET "CommonStockId" = NULL WHERE "Id" = {issuer.Id};
                DELETE FROM "CommonStock" WHERE "Id" = {issuer.Id};
                """
            );
        }
        (await Snapshot()).Should().Be(before);
        (await DbContext.Set<CommonStock>().CountAsync()).Should().Be(0);
        var constraints = await DbContext
            .Database.SqlQueryRaw<int>(
                """
                SELECT count(*)::int AS "Value" FROM pg_constraint
                WHERE conname = ANY(ARRAY[
                    'FK_CommonStockCusipAlias_EquityIssuer_CommonStockId',
                    'FK_CommonStockListedCusip_EquityIssuer_CommonStockId',
                    'FK_CommonStockTickerAlias_EquityIssuer_CommonStockId',
                    'FK_CommonStockDelistedListing_EquityIssuer_CommonStockId'])
                  AND confrelid = '"EquityIssuer"'::regclass AND confdeltype = 'r' AND convalidated
                """
            )
            .SingleAsync();
        constraints.Should().Be(4);
    }

    private async Task<string> Snapshot()
    {
        var rows = new List<string>();
        foreach (var table in Tables)
            rows.Add(
                await DbContext
                    .Database.SqlQueryRaw<string>(
                        $"""SELECT COALESCE(jsonb_agg(to_jsonb(row) ORDER BY "Id"), '[]'::jsonb)::text AS "Value" FROM "{table}" row"""
                    )
                    .SingleAsync()
            );
        return string.Join("\n", rows);
    }
}
