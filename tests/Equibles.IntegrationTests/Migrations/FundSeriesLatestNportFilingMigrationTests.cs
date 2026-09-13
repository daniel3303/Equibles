using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.IntegrationTests.Migrations;

[Collection(ParadeDbCollection.Name)]
public class FundSeriesLatestNportFilingMigrationTests : ParadeDbMcpTestBase
{
    public FundSeriesLatestNportFilingMigrationTests(ParadeDbFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Up_BackfillsEveryIdentityPopulationAndHighestAccession()
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        // The migration operates on these historical columns; do not downgrade unrelated current storage.
        await DbContext.Database.ExecuteSqlRawAsync("""
            CREATE TEMP TABLE "FundSeries" (
              "IdentityKey" text PRIMARY KEY, "SeriesId" text, "CommonStockId" uuid,
              "RegistrantCik" text, "LatestReportPeriodDate" date, "LatestFilingDate" date
            ) ON COMMIT DROP;
            CREATE TEMP TABLE "NportFiling" (
              "Id" uuid PRIMARY KEY, "AccessionNumber" text, "SeriesId" text, "CommonStockId" uuid,
              "RegistrantCik" text, "ReportPeriodDate" date, "FilingDate" date
            ) ON COMMIT DROP;
            INSERT INTO "FundSeries" VALUES
              ('cross', 'S-CROSS', NULL, '000100', '2026-06-30', '2026-08-01'),
              ('tracked-idless', '', '00000000-0000-0000-0000-000000000010', NULL, '2026-06-30', '2026-08-01'),
              ('trust-idless', '', NULL, '000200', '2026-06-30', '2026-08-01');
            INSERT INTO "NportFiling" VALUES
              ('00000000-0000-0000-0000-000000000001', '0001', 'S-CROSS', '00000000-0000-0000-0000-000000000020', NULL, '2026-06-30', '2026-08-01'),
              ('00000000-0000-0000-0000-000000000002', '0002', 'S-CROSS', NULL, '000100', '2026-06-30', '2026-08-01'),
              ('00000000-0000-0000-0000-000000000003', '1001', NULL, '00000000-0000-0000-0000-000000000010', NULL, '2026-06-30', '2026-08-01'),
              ('00000000-0000-0000-0000-000000000004', '1002', '', '00000000-0000-0000-0000-000000000010', NULL, '2026-06-30', '2026-08-01'),
              ('00000000-0000-0000-0000-000000000005', '2001', NULL, NULL, '000200', '2026-06-30', '2026-08-01'),
              ('00000000-0000-0000-0000-000000000006', '2002', '', NULL, '000200', '2026-06-30', '2026-08-01');
            """);
        var commands = DbContext.GetService<IMigrationsSqlGenerator>()
            .Generate(new AddFundSeriesLatestNportFiling().UpOperations);
        foreach (var command in commands)
            await DbContext.Database.ExecuteSqlRawAsync(command.CommandText);
        var references = await DbContext.Database.SqlQueryRaw<FundSeriesReference>(
            """SELECT "IdentityKey", "LatestNportFilingId" FROM "FundSeries" """)
            .ToDictionaryAsync(row => row.IdentityKey, row => row.LatestNportFilingId);
        references["cross"].Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        references["tracked-idless"].Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000004"));
        references["trust-idless"].Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000006"));
        references.Values.Should().OnlyHaveUniqueItems();
    }

    public class FundSeriesReference
    {
        public string IdentityKey { get; set; }
        public Guid LatestNportFilingId { get; set; }
    }
}
