using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class NativeIssuerCheckpointTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    [Fact]
    public async Task Retargeting_PreservesEveryCheckpointField_AndSurvivesLegacyOwnerRemoval()
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        // Frozen pre-cutover tables exercise the actual migration without downgrading applied history.
        await DbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TEMP TABLE "CommonStock" ("Id" uuid PRIMARY KEY) ON COMMIT DROP;
            CREATE TEMP TABLE "EquityIssuer" ("Id" uuid PRIMARY KEY) ON COMMIT DROP;
            INSERT INTO "CommonStock" VALUES ('00000000-0000-0000-0000-000000000001');
            INSERT INTO "EquityIssuer" SELECT * FROM "CommonStock";
            CREATE TEMP TABLE "CompanyFilingSyncState" (
                "CommonStockId" uuid PRIMARY KEY, "LastSyncedAt" timestamptz NOT NULL,
                CONSTRAINT "FK_CompanyFilingSyncState_CommonStock_CommonStockId"
                FOREIGN KEY ("CommonStockId") REFERENCES "CommonStock"("Id") ON DELETE CASCADE
            ) ON COMMIT DROP;
            CREATE TEMP TABLE "FinancialFactsSyncStatus" (
                "Id" uuid PRIMARY KEY, "CommonStockId" uuid NOT NULL UNIQUE, "LastCheckedAt" timestamptz NOT NULL,
                "LastFiledDateSeen" date, "ImporterVersion" integer NOT NULL,
                "CalendarEvidenceFingerprint" varchar(64), "ConceptMetadataCheckedAt" timestamptz,
                CONSTRAINT "FK_FinancialFactsSyncStatus_CommonStock_CommonStockId"
                FOREIGN KEY ("CommonStockId") REFERENCES "CommonStock"("Id") ON DELETE CASCADE
            ) ON COMMIT DROP;
            CREATE TEMP TABLE "TranscriptCheckStatuses" (
                "Id" uuid PRIMARY KEY, "CommonStockId" uuid NOT NULL UNIQUE, "LastCheckedAt" timestamptz NOT NULL,
                "HasTranscripts" boolean NOT NULL,
                CONSTRAINT "FK_TranscriptCheckStatuses_CommonStock_CommonStockId"
                FOREIGN KEY ("CommonStockId") REFERENCES "CommonStock"("Id") ON DELETE CASCADE
            ) ON COMMIT DROP;
            INSERT INTO "CompanyFilingSyncState" VALUES
                ('00000000-0000-0000-0000-000000000001', '2025-09-02T03:04:05.123456Z');
            INSERT INTO "FinancialFactsSyncStatus" VALUES
                ('00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000001',
                '2025-09-03T03:04:05.123456Z', '2025-09-01', 17, 'source-calendar-evidence', '2025-09-04T03:04:05.123456Z');
            INSERT INTO "TranscriptCheckStatuses" VALUES
                ('00000000-0000-0000-0000-000000000003', '00000000-0000-0000-0000-000000000001',
                '2025-09-05T03:04:05.123456Z', true);
            """
        );
        var before = await Snapshot();
        var commands = DbContext
            .GetService<IMigrationsSqlGenerator>()
            .Generate(new RetargetIssuerIngestionCheckpoints().UpOperations);
        foreach (var command in commands)
            await DbContext.Database.ExecuteSqlRawAsync(command.CommandText);
        (await Snapshot()).Should().Be(before);
        await DbContext.Database.ExecuteSqlRawAsync("""DELETE FROM "CommonStock";""");
        (await Snapshot()).Should().Be(before);
        Func<Task> removeIssuer = () =>
            DbContext.Database.ExecuteSqlRawAsync("""DELETE FROM "EquityIssuer";""");
        (await removeIssuer.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task NativeIssuerWithoutAListing_CanKeepAndUpdateItsIngestionWatermarks()
    {
        var issuer = new EquityIssuer { Name = "Issuer without a U.S. listing" };
        DbContext.Add(issuer);
        await DbContext.SaveChangesAsync();
        var instant = new DateTime(2025, 9, 1, 2, 3, 4, DateTimeKind.Utc);
        DbContext.Add(new CompanyFilingSyncState { Issuer = issuer, LastSyncedAt = instant });
        DbContext.Add(
            new TranscriptCheckStatus
            {
                Issuer = issuer,
                LastCheckedAt = instant,
                HasTranscripts = true,
            }
        );
        await DbContext.SaveChangesAsync();
        var original = new FinancialFactsSyncStatus
        {
            EquityIssuerId = issuer.Id,
            LastCheckedAt = instant,
            ImporterVersion = 17,
            LastFiledDateSeen = new DateOnly(2025, 8, 31),
            CalendarEvidenceFingerprint = "original",
            ConceptMetadataCheckedAt = instant.AddDays(-1),
        };
        await DbContext
            .Set<FinancialFactsSyncStatus>()
            .UpsertRange(original)
            .On(row => row.EquityIssuerId)
            .RunAsync();
        await DbContext
            .Set<FinancialFactsSyncStatus>()
            .UpsertRange(
                new FinancialFactsSyncStatus
                {
                    EquityIssuerId = issuer.Id,
                    LastCheckedAt = instant.AddDays(1),
                    ImporterVersion = 18,
                }
            )
            .On(row => row.EquityIssuerId)
            .WhenMatched(
                (existing, incoming) =>
                    new FinancialFactsSyncStatus
                    {
                        LastCheckedAt = incoming.LastCheckedAt,
                        ImporterVersion = incoming.ImporterVersion,
                    }
            )
            .RunAsync();
        var stored = await new FinancialFactsSyncStatusRepository(DbContext)
            .GetByIssuerId(issuer.Id)
            .SingleAsync();
        stored.Id.Should().Be(original.Id);
        stored.LastCheckedAt.Should().Be(instant.AddDays(1));
        stored.ImporterVersion.Should().Be(18);
        stored.LastFiledDateSeen.Should().Be(original.LastFiledDateSeen);
        stored.CalendarEvidenceFingerprint.Should().Be("original");
        stored.ConceptMetadataCheckedAt.Should().Be(original.ConceptMetadataCheckedAt);
        (
            await new CompanyFilingSyncStateRepository(DbContext)
                .GetByIssuerId(issuer.Id)
                .SingleAsync()
        )
            .LastSyncedAt.Should()
            .Be(instant);
        (await DbContext.Set<TranscriptCheckStatus>().SingleAsync())
            .HasTranscripts.Should()
            .BeTrue();
        (await DbContext.Set<CommonStock>().CountAsync()).Should().Be(0);
    }

    private Task<string> Snapshot() =>
        DbContext
            .Database.SqlQueryRaw<string>(
                """
                SELECT jsonb_build_array(
                    (SELECT jsonb_agg(to_jsonb(s) ORDER BY "CommonStockId") FROM "CompanyFilingSyncState" s),
                    (SELECT jsonb_agg(to_jsonb(s) ORDER BY "Id") FROM "FinancialFactsSyncStatus" s),
                    (SELECT jsonb_agg(to_jsonb(s) ORDER BY "Id") FROM "TranscriptCheckStatuses" s)
                )::text AS "Value"
                """
            )
            .SingleAsync();
}
