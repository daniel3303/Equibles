using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Equibles.IntegrationTests.Migrations;

[Collection(ParadeDbCollection.Name)]
public class RepairChunkReportingDatesMigrationTests : ParadeDbMcpTestBase
{
    public RepairChunkReportingDatesMigrationTests(ParadeDbFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Up_ReplacesAStaleChunkCacheWithItsDocumentReportingDate()
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        // Frozen historical table shapes keep this test independent of later irreversible migrations.
        await DbContext.Database.ExecuteSqlRawAsync("""
            CREATE TEMP TABLE "Document" ("Id" uuid PRIMARY KEY, "ReportingDate" date) ON COMMIT DROP;
            CREATE TEMP TABLE "Chunk" ("Id" uuid PRIMARY KEY, "DocumentId" uuid, "ReportingDate" timestamptz) ON COMMIT DROP;
            INSERT INTO "Document" VALUES ('00000000-0000-0000-0000-000000000001', '2023-09-30');
            INSERT INTO "Chunk" VALUES ('00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000001', '2023-12-31T00:00:00Z');
            """);
        var commands = DbContext.GetService<IMigrationsSqlGenerator>()
            .Generate(new RepairChunkReportingDates().UpOperations);
        foreach (var command in commands)
            await DbContext.Database.ExecuteSqlRawAsync(command.CommandText);
        var repaired = await DbContext.Database.SqlQueryRaw<DateTime>("""SELECT "ReportingDate" AS "Value" FROM "Chunk" """)
            .SingleAsync();
        repaired.Should().Be(new DateTime(2023, 9, 30, 0, 0, 0, DateTimeKind.Utc));
    }
}
