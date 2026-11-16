using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Holdings;

// Pins the partial index built for the holder-rank aggregate (#6049) while it remains in
// the schema. The rank now also filters ShareType and reads the per-stock exposure index;
// this index is dropped by a migration once production shows no scans of it.
public class HoldingsModuleStockQuarterCommonValueIndexTests
{
    private const string IndexName = "IX_InstitutionalHolding_StockQuarterCommonValue";
    private const string IndexIncludeAnnotation = "Npgsql:IndexInclude";
    private const string IndexConcurrentAnnotation = "Npgsql:CreatedConcurrently";

    [Fact]
    public void StockQuarterCommonValueIndex_KeepsItsShapeUntilDropped()
    {
        using var db = NewDb();

        var entity = db.Model.FindEntityType(typeof(InstitutionalHolding));
        entity.Should().NotBeNull();

        var index = entity.GetIndexes().Single(i => i.GetDatabaseName() == IndexName);

        index
            .Properties.Select(p => p.Name)
            .Should()
            .Equal(
                nameof(InstitutionalHolding.EquityIssuerId),
                nameof(InstitutionalHolding.ReportDate),
                nameof(InstitutionalHolding.InstitutionalHolderId)
            );
        index
            .FindAnnotation(IndexIncludeAnnotation)
            ?.Value.Should()
            .BeEquivalentTo(new[] { nameof(InstitutionalHolding.Value) });
        index.GetFilter().Should().Be("\"FilingType\" = 0 AND \"OptionType\" IS NULL");
        index
            .FindAnnotation(IndexConcurrentAnnotation)
            ?.Value.Should()
            .Be(true, "the live holdings table must remain writable while the index builds");
    }

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new HoldingsModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
            }
        );
    }
}
