using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.IntegrationTests.Migrations;

[Collection(ParadeDbCollection.Name)]
public class FundSeriesLatestNportFilingMigrationTests : ParadeDbMcpTestBase
{
    private const string PreviousMigration = "20260825134832_AddDocumentChunkAttempts";
    private const string ReferenceMigration = "20260828024704_AddFundSeriesLatestNportFiling";

    public FundSeriesLatestNportFilingMigrationTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Up_BackfillsEveryIdentityPopulationAndHighestAccession()
    {
        var migrator = DbContext.Database.GetService<IMigrator>();

        try
        {
            var trackedSeriesStock = Stock("SERIES");
            var trackedIdlessStock = Stock("IDLESS");
            var reportDate = new DateOnly(2026, 6, 30);
            var filingDate = new DateOnly(2026, 8, 1);
            var crossPopulationLower = Filing(
                "0001",
                "S-CROSS",
                trackedSeriesStock.Id,
                null,
                reportDate,
                filingDate
            );
            var crossPopulationHigher = Filing(
                "0002",
                "S-CROSS",
                null,
                "000100",
                reportDate,
                filingDate
            );
            var trackedIdlessLower = Filing(
                "1001",
                null,
                trackedIdlessStock.Id,
                null,
                reportDate,
                filingDate
            );
            var trackedIdlessHigher = Filing(
                "1002",
                "",
                trackedIdlessStock.Id,
                null,
                reportDate,
                filingDate
            );
            var trustIdlessLower = Filing(
                "2001",
                null,
                null,
                "000200",
                reportDate,
                filingDate
            );
            var trustIdlessHigher = Filing(
                "2002",
                "",
                null,
                "000200",
                reportDate,
                filingDate
            );

            DbContext.AddRange(
                trackedSeriesStock,
                trackedIdlessStock,
                crossPopulationLower,
                crossPopulationHigher,
                trackedIdlessLower,
                trackedIdlessHigher,
                trustIdlessLower,
                trustIdlessHigher,
                Series("cross", "S-CROSS", null, "000100", reportDate, filingDate),
                Series("tracked-idless", "", trackedIdlessStock.Id, null, reportDate, filingDate),
                Series("trust-idless", "", null, "000200", reportDate, filingDate)
            );
            await DbContext.SaveChangesAsync();
            DbContext.ChangeTracker.Clear();

            await migrator.MigrateAsync(PreviousMigration);
            await migrator.MigrateAsync(ReferenceMigration);
            DbContext.ChangeTracker.Clear();

            var references = await DbContext
                .Set<FundSeries>()
                .ToDictionaryAsync(series => series.IdentityKey, series => series.LatestNportFilingId);
            references["cross"].Should().Be(crossPopulationHigher.Id);
            references["tracked-idless"].Should().Be(trackedIdlessHigher.Id);
            references["trust-idless"].Should().Be(trustIdlessHigher.Id);
            references.Values.Should().OnlyHaveUniqueItems();
        }
        finally
        {
            await migrator.MigrateAsync();
        }
    }

    private static CommonStock Stock(string ticker) =>
        new()
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = ticker,
            Cik = Guid.NewGuid().ToString("N")[..10],
        };

    private static NportFiling Filing(
        string accession,
        string seriesId,
        Guid? commonStockId,
        string registrantCik,
        DateOnly reportDate,
        DateOnly filingDate
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccessionNumber = accession,
            SeriesId = seriesId,
            CommonStockId = commonStockId,
            RegistrantCik = registrantCik,
            ReportPeriodDate = reportDate,
            FilingDate = filingDate,
        };

    private static FundSeries Series(
        string identityKey,
        string seriesId,
        Guid? commonStockId,
        string registrantCik,
        DateOnly reportDate,
        DateOnly filingDate
    ) =>
        new()
        {
            IdentityKey = identityKey,
            Slug = identityKey,
            LatestNportFilingId = Guid.NewGuid(),
            SeriesId = seriesId,
            CommonStockId = commonStockId,
            RegistrantCik = registrantCik,
            LatestReportPeriodDate = reportDate,
            LatestFilingDate = filingDate,
        };
}
