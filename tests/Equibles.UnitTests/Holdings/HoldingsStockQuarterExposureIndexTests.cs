using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Holdings;

public class HoldingsStockQuarterExposureIndexTests
{
    [Fact]
    public void StockQuarterQueries_CoverListingAndPositionFiltersWithoutHeapReads()
    {
        using var db = new EquiblesFinancialDbContext(
            new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new IModuleConfiguration[]
            {
                new HoldingsModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
            }
        );
        var index = db
            .Model.FindEntityType(typeof(InstitutionalHolding))
            .GetIndexes()
            .Single(i =>
                i.Properties.Select(p => p.Name)
                    .SequenceEqual(new[] { "EquityIssuerId", "ReportDate" })
            );
        index.GetDatabaseName().Should().Be("IX_InstitutionalHolding_StockQuarterExposure");
        ((IReadOnlyList<string>)index.FindAnnotation("Npgsql:IndexInclude").Value)
            .Should()
            .BeEquivalentTo(
                "InstitutionalHolderId",
                "Value",
                "Shares",
                "ListedTicker",
                "FilingType",
                "OptionType"
            );
        index.FindAnnotation("Npgsql:CreatedConcurrently").Value.Should().Be(true);
    }
}
