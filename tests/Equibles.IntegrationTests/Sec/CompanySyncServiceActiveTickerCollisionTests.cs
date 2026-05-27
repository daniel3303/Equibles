using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.HostedService.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Sibling to <see cref="CompanySyncServiceUpdateExistingTests"/> (the no-collision
/// update). This pins UpdateExistingStock's active-collision guard (zero-hit):
/// when a company's new primary ticker is still held by ANOTHER company that is
/// itself active in SEC's feed, the update must be skipped — not applied — to
/// preserve the globally-unique-primary-ticker invariant. A regression dropping
/// that guard would let two stocks claim the same primary ticker.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CompanySyncServiceActiveTickerCollisionTests : ParadeDbMcpTestBase
{
    public CompanySyncServiceActiveTickerCollisionTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SyncCompaniesFromSecApi_NewTickerHeldByAnotherActiveCompany_SkipsUpdate()
    {
        // mover (CIK A, "OLD") wants "AAPL"; holder (CIK B) already owns "AAPL"
        // and is still in the SEC feed — so the rename must be refused.
        var mover = new CommonStock
        {
            Cik = "0000000001",
            Ticker = "OLD",
            Name = "Mover Inc.",
        };
        var holder = new CommonStock
        {
            Cik = "0000000002",
            Ticker = "AAPL",
            Name = "Holder Inc.",
        };
        DbContext.Add(mover);
        DbContext.Add(holder);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .GetActiveCompanies()
            .Returns(
                new List<CompanyInfo>
                {
                    new()
                    {
                        Cik = "0000000001",
                        Name = "Mover Inc.",
                        Tickers = ["AAPL"],
                        EntityType = "operating",
                    },
                    new()
                    {
                        Cik = "0000000002",
                        Name = "Holder Inc.",
                        Tickers = ["AAPL"],
                        EntityType = "operating",
                    },
                }
            );

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(CommonStockRepository), new CommonStockRepository(DbContext)),
            (
                typeof(CommonStockManager),
                new CommonStockManager(
                    new CommonStockRepository(DbContext),
                    Substitute.For<IPublishEndpoint>()
                )
            ),
            (typeof(EquiblesFinancialDbContext), DbContext)
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

        // The collision is refused: the mover keeps "OLD", the holder keeps "AAPL".
        await using var verify = Fixture.CreateDbContext();
        var moverRow = await verify
            .Set<CommonStock>()
            .AsNoTracking()
            .SingleAsync(s => s.Cik == "0000000001");
        moverRow.Ticker.Should().Be("OLD");
    }
}
