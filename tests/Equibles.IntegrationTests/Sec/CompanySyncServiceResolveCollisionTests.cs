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
/// Pins <c>ResolveTickerCollision</c> end-to-end. Sibling tests pin CreateNew,
/// UpdateExisting, ReplaceObsolete. This pins the case where two CIKs in SEC's
/// feed BOTH claim the same primary ticker (parent + subsidiary pattern, e.g.
/// ATAI Life Sciences + AtaiBeckley sharing ATAI): the incumbent wins, and the
/// incoming subsidiary's CIK lands in the incumbent's <c>SecondaryCiks</c> so
/// its filings still flow through. A regression that delete-and-replaced
/// instead of attaching would destroy the incumbent's filing history every
/// sync until the collision resolves.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CompanySyncServiceResolveCollisionTests : ParadeDbMcpTestBase
{
    public CompanySyncServiceResolveCollisionTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SyncCompaniesFromSecApi_TwoActiveCiksShareTicker_AttachesSubsidiaryToIncumbentSecondaryCiks()
    {
        // Seed the incumbent (parent) with primary ticker ATAI and a lower CIK
        // so the priority chain falls through to the CIK tiebreak (both listed +
        // operating in the metadata returned below).
        var incumbent = new CommonStock
        {
            Cik = "0001719395",
            Ticker = "ATAI",
            Name = "ATAI Life Sciences N.V.",
        };
        DbContext.Add(incumbent);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        // SEC feed returns BOTH the incumbent and the incoming subsidiary, each
        // claiming ATAI. The order is incumbent first so the parent is processed
        // through UpdateExistingStock; the subsidiary then hits the
        // ReplaceObsolete branch which routes to ResolveTickerCollision.
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .GetActiveCompanies()
            .Returns(
                new List<CompanyInfo>
                {
                    new()
                    {
                        Cik = "0001719395",
                        Name = "ATAI Life Sciences N.V.",
                        Tickers = ["ATAI"],
                        EntityType = "operating",
                    },
                    new()
                    {
                        Cik = "0001999999",
                        Name = "AtaiBeckley Subsidiary",
                        Tickers = ["ATAI"],
                        EntityType = "operating",
                    },
                }
            );
        // Both metadata records satisfy IsListed (a real exchange) and
        // IsOperatingCompany (entityType "operating"), so the priority chain
        // falls to the lower-CIK tiebreak — incumbent (lower) wins.
        secEdgarClient
            .GetCompanyMetadata("0001719395")
            .Returns(
                new CompanyMetadata
                {
                    Cik = "0001719395",
                    EntityType = "operating",
                    Exchanges = ["Nasdaq"],
                }
            );
        secEdgarClient
            .GetCompanyMetadata("0001999999")
            .Returns(
                new CompanyMetadata
                {
                    Cik = "0001999999",
                    EntityType = "operating",
                    Exchanges = ["Nasdaq"],
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

        await using var verify = Fixture.CreateDbContext();
        var stocks = await verify.Set<CommonStock>().AsNoTracking().ToListAsync();
        // Only the incumbent must remain; the subsidiary CIK is attached as a
        // SecondaryCik so its filings still flow through.
        stocks.Should().ContainSingle();
        stocks[0].Cik.Should().Be("0001719395");
        stocks[0].SecondaryCiks.Should().Contain("0001999999");
    }
}
