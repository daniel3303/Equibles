using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Holdings;

// Pins the per-holder covering index shape that keeps the institution-profile and
// per-stock-holder pages off the 30s command timeout (#3605). The holder pages issue
// `SELECT max("FilingDate") WHERE "InstitutionalHolderId" = @h AND "ReportDate" = @d`;
// unless FilingDate rides along in the (InstitutionalHolderId, ReportDate) covering
// index's INCLUDE list, Postgres can't seek that holder+quarter and aggregate FilingDate
// index-only, so it falls back to a backward scan of the FilingDate index that filters
// out millions of rows and 500s. This pins FilingDate into the INCLUDE list so the regression
// can't silently come back.
public class HoldingsModuleHolderCoveringIndexIncludesFilingDateTests
{
    // The Npgsql provider stores a covering index's INCLUDE columns under this annotation;
    // reading it directly keeps the assertion provider-agnostic (the model builds under the
    // in-memory provider but the annotation is set at model-build time regardless).
    private const string IndexIncludeAnnotation = "Npgsql:IndexInclude";

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[] { new HoldingsModuleConfiguration() }
        );
    }

    [Fact]
    public void HolderReportDateIndex_CoversFilingDate_SoTheLatestFilingProbeIsIndexOnly()
    {
        using var db = NewDb();

        var entity = db.Model.FindEntityType(typeof(InstitutionalHolding));
        entity.Should().NotBeNull();

        var holderReportDateIndex = entity
            .GetIndexes()
            .Single(i =>
                i.Properties.Select(p => p.Name)
                    .SequenceEqual(
                        new[]
                        {
                            nameof(InstitutionalHolding.InstitutionalHolderId),
                            nameof(InstitutionalHolding.ReportDate),
                        }
                    )
            );

        var includedColumns =
            holderReportDateIndex.FindAnnotation(IndexIncludeAnnotation)?.Value
            as IReadOnlyList<string>;

        includedColumns
            .Should()
            .NotBeNull("the holder page rollups rely on a covering index, not a heap scan")
            .And.Contain(
                nameof(InstitutionalHolding.FilingDate),
                "max(FilingDate) for a holder+quarter must be served index-only (#3605)"
            )
            .And.Contain(
                nameof(InstitutionalHolding.Value),
                "the existing portfolio-rollup INCLUDE columns must stay"
            )
            .And.Contain(nameof(InstitutionalHolding.Shares))
            .And.Contain(nameof(InstitutionalHolding.CommonStockId));
    }
}
