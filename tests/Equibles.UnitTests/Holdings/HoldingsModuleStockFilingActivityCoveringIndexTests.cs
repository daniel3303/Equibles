using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Holdings;

// Pins the stock shell's recent-filing access path. Without a stock+filing-date
// covering index, Postgres scans every historical holding for the stock through
// the ReportDate index, heap-filters the 30-day window, and times out under 13F
// ingest pressure (#6049).
public class HoldingsModuleStockFilingActivityCoveringIndexTests
{
    private const string IndexIncludeAnnotation = "Npgsql:IndexInclude";
    private const string IndexConcurrentAnnotation = "Npgsql:CreatedConcurrently";

    [Fact]
    public void StockFilingDateIndex_CoversBothDistinctCountKeys()
    {
        using var db = NewDb();

        var entity = db.Model.FindEntityType(typeof(InstitutionalHolding));
        entity.Should().NotBeNull();

        var stockFilingDateIndex = entity
            .GetIndexes()
            .Single(i =>
                i.Properties.Select(p => p.Name)
                    .SequenceEqual(
                        new[]
                        {
                            nameof(InstitutionalHolding.CommonStockId),
                            nameof(InstitutionalHolding.FilingDate),
                        }
                    )
            );

        var includedColumns =
            stockFilingDateIndex.FindAnnotation(IndexIncludeAnnotation)?.Value
            as IReadOnlyList<string>;

        includedColumns
            .Should()
            .NotBeNull("the recent-filing aggregate must avoid heap reads under ingest pressure")
            .And.Contain(nameof(InstitutionalHolding.AccessionNumber))
            .And.Contain(nameof(InstitutionalHolding.InstitutionalHolderId));

        stockFilingDateIndex
            .FindAnnotation(IndexConcurrentAnnotation)
            ?.Value.Should()
            .Be(true, "a multi-million-row holdings index must not block live ingest writes");
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
