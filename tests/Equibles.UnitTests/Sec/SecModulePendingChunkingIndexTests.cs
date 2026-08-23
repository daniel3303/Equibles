using Equibles.CommonStocks.Data;
using Equibles.Data;
using Equibles.Media.Data;
using Equibles.Sec.Data;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Sec;

public class SecModulePendingChunkingIndexTests
{
    [Fact]
    public void PendingChunkingIndex_IsFilteredAndOrdered()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only", o => o.UseVector())
            .EnableServiceProviderCaching(false)
            .Options;
        using var db = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new MediaModuleConfiguration(),
                new SecModuleConfiguration(),
            }
        );

        var entity = db.Model.FindEntityType(typeof(Document));
        var index = entity!
            .GetIndexes()
            .Single(i => i.GetDatabaseName() == "IX_Document_PendingChunking");

        index.Properties.Select(p => p.Name).Should().Equal("CreationTime", "Id");
        index.GetFilter().Should().Be("\"ChunkedAt\" IS NULL");
    }
}
