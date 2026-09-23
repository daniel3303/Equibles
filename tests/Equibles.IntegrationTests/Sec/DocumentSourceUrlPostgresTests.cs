using Equibles.CommonStocks.Data;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Equibles.IntegrationTests.Sec;

public class DocumentSourceUrlPostgresTests
{
    [Fact]
    public async Task RetainsLongOfficialSourceUrlWithoutTruncatingItsQuery()
    {
        await using var database = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await database.StartAsync();
        await using var context = new EquiblesFinancialDbContext(
            new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
                .UseNpgsql(database.GetConnectionString())
                .Options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new MediaModuleConfiguration(),
                new SecTestModuleConfiguration(),
            }
        );
        await context.Database.OpenConnectionAsync();
        await using (var command = context.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = context
                .Database.GenerateCreateScript()
                .Replace("CREATE EXTENSION IF NOT EXISTS vector;", "", StringComparison.Ordinal);
            await command.ExecuteNonQueryAsync();
        }
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "SOURCE",
            Name: "Source issuer",
            Cik: "123456"
        );
        var url = "https://authority.example/reports/download?reference=" + new string('a', 1500);
        var document = new Document
        {
            Id = Guid.NewGuid(),
            Issuer = issuer,
            Content = new Equibles.Media.Data.Models.File
            {
                Id = Guid.NewGuid(),
                Name = "official-report",
                Extension = "txt",
                ContentType = "text/plain",
            },
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2026, 3, 1),
            ReportingForDate = new DateOnly(2025, 12, 31),
            SourceUrl = url,
        };
        context.Add(document);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var retained = await context.Set<Document>().SingleAsync(row => row.Id == document.Id);
        retained.SourceUrl.Should().Be(url);
    }
}
