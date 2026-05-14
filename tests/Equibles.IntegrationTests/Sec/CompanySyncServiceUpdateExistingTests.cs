using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.IntegrationTests.Helpers;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.HostedService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Sibling to <see cref="CompanySyncServiceTests"/> which pins the CreateNewStock
/// branch. This pins UpdateExistingStock: a SEC company whose CIK already exists
/// in the DB with a different primary ticker must update in-place — the CIK is
/// the immutable identifier; the ticker can change (e.g., a corporate ticker
/// reassignment). A regression that created a new row instead of updating would
/// produce duplicate CIK records and break the unique-by-CIK invariant.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CompanySyncServiceUpdateExistingTests : ParadeDbMcpTestBase
{
    public CompanySyncServiceUpdateExistingTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SyncCompaniesFromSecApi_ExistingCikWithDifferentTicker_UpdatesInPlace()
    {
        // Seed an existing stock whose CIK appears in the next SEC sync payload.
        var existing = new CommonStock
        {
            Cik = "0001067983",
            Ticker = "OLD",
            Name = "Old Name Inc.",
            SecondaryTickers = ["X.A"],
            Description = "Pre-sync description",
        };
        DbContext.Add(existing);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient.GetActiveCompanies().Returns(new List<CompanyInfo>
        {
            new()
            {
                Cik = "0001067983",
                Name = "Berkshire Hathaway Inc.",
                Tickers = ["BRK.A", "BRK.B"],
                EntityType = "operating",
            },
        });

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(CommonStockRepository), new CommonStockRepository(DbContext)),
            (
                typeof(CommonStockManager),
                new CommonStockManager(new CommonStockRepository(DbContext))
            ),
            (typeof(EquiblesDbContext), DbContext)
        );

        var sut = new CompanySyncService(
            scopeFactory,
            secEdgarClient,
            Options.Create(new WorkerOptions { TickersToSync = [] }),
            Substitute.For<ILogger<CompanySyncService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        await sut.SyncCompaniesFromSecApi();

        // Re-read from a fresh context — the row must be the SAME row (same Id)
        // with updated ticker/name/secondaries, NOT a new duplicate.
        await using var verify = Fixture.CreateDbContext();
        var stocks = await verify.Set<CommonStock>().AsNoTracking().ToListAsync();
        stocks.Should().ContainSingle("CIK must be unique — never duplicated");
        stocks[0].Id.Should().Be(existing.Id);
        stocks[0].Ticker.Should().Be("BRK.A");
        stocks[0].Name.Should().Be("Berkshire Hathaway Inc.");
        stocks[0].SecondaryTickers.Should().Equal("BRK.B");
    }
}
