using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Migrations;

[Collection(ParadeDbCollection.Name)]
public class RepairChunkReportingDatesMigrationTests : ParadeDbMcpTestBase
{
    private const string PreviousMigration = "20260810162249_AddNportReportedHoldingCounts";
    private const string RepairMigration = "20260810212201_RepairChunkReportingDates";

    public RepairChunkReportingDatesMigrationTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Up_ReplacesAStaleChunkCacheWithItsDocumentReportingDate()
    {
        var migrator = DbContext.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        try
        {
            var stock = new CommonStock
            {
                Ticker = "AAPL",
                Name = "Apple Inc.",
                Cik = "0000320193",
            };
            var file = new File
            {
                Name = "transcript",
                Extension = "txt",
                ContentType = "text/plain",
                Size = 1,
                FileContent = new FileContent { Bytes = [0x01] },
            };
            var document = new Document
            {
                CommonStock = stock,
                Content = file,
                DocumentType = DocumentType.TenK,
                ReportingDate = new DateOnly(2023, 9, 30),
                ReportingForDate = new DateOnly(2023, 9, 30),
                LineCount = 1,
            };
            var chunk = new Chunk
            {
                Document = document,
                Index = 0,
                StartPosition = 0,
                EndPosition = 10,
                StartLineNumber = 1,
                Content = "transcript",
                DocumentType = document.DocumentType,
                Ticker = stock.Ticker,
                ReportingDate = new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            };
            DbContext.Add(chunk);
            await DbContext.SaveChangesAsync();

            await migrator.MigrateAsync(RepairMigration);
            DbContext.ChangeTracker.Clear();

            var repaired = await DbContext.Set<Chunk>().FindAsync(chunk.Id);
            repaired.ReportingDate.Should().Be(
                new DateTime(2023, 9, 30, 0, 0, 0, DateTimeKind.Utc)
            );
        }
        finally
        {
            await migrator.MigrateAsync(RepairMigration);
        }
    }
}
