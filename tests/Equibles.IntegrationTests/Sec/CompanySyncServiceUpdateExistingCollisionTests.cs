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
/// <see cref="CompanySyncServiceUpdateExistingTests"/> pins only the collision-free
/// in-place update. This pins the two primary-ticker collision arms of
/// <c>UpdateExistingStock</c> plus the no-op early return:
///
/// <list type="bullet">
/// <item>The ticker the SEC now assigns to an existing CIK is held by another
/// company whose own CIK has dropped out of the SEC feed → that obsolete holder
/// is deleted and the update proceeds.</item>
/// <item>The ticker is held by a company still active in the SEC feed → the
/// update is skipped (the active holder keeps the ticker) and the stale row is
/// left untouched.</item>
/// <item>The SEC payload matches the stored row exactly → no write at all.</item>
/// </list>
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CompanySyncServiceUpdateExistingCollisionTests : ParadeDbMcpTestBase
{
    public CompanySyncServiceUpdateExistingCollisionTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private CompanySyncService BuildService(ISecEdgarClient secEdgarClient)
    {
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

        return new CompanySyncService(
            scopeFactory,
            secEdgarClient,
            Options.Create(new WorkerOptions { TickersToSync = [] }),
            Substitute.For<ILogger<CompanySyncService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );
    }

    [Fact]
    public async Task SyncCompaniesFromSecApi_TickerHeldByCompanyNotInSecFeed_DeletesObsoleteHolderThenUpdates()
    {
        // Holder owns ticker "NEW" but its CIK is absent from the SEC payload —
        // it is obsolete and must be removed so the updating company can take
        // the ticker.
        var obsoleteHolder = new CommonStock
        {
            Cik = "0000000001",
            Ticker = "NEW",
            Name = "Obsolete Holder Inc.",
            Description = "Holds the ticker the updater wants",
        };
        var updating = new CommonStock
        {
            Cik = "0001067983",
            Ticker = "OLD",
            Name = "Old Name Inc.",
            Description = "Will move from OLD to NEW",
        };
        DbContext.AddRange(obsoleteHolder, updating);
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
                        Cik = "0001067983",
                        Name = "Berkshire Hathaway Inc.",
                        Tickers = ["NEW"],
                        EntityType = "operating",
                    },
                }
            );

        await BuildService(secEdgarClient).SyncCompaniesFromSecApi();

        await using var verify = Fixture.CreateDbContext();
        var stocks = await verify.Set<CommonStock>().AsNoTracking().ToListAsync();
        stocks.Should().ContainSingle("the obsolete holder must be deleted");
        stocks[0].Id.Should().Be(updating.Id);
        stocks[0].Ticker.Should().Be("NEW");
        stocks[0].Name.Should().Be("Berkshire Hathaway Inc.");
    }

    [Fact]
    public async Task SyncCompaniesFromSecApi_TickerHeldByActiveCompany_SkipsUpdateAndLeavesRowStale()
    {
        // Holder owns ticker "NEW" AND its CIK is still in the SEC payload — it
        // is active, keeps the ticker, and the updating company must be skipped.
        // The holder's own payload matches its stored row exactly, exercising
        // the needsUpdate==false early return on its iteration.
        var activeHolder = new CommonStock
        {
            Cik = "0000000001",
            Ticker = "NEW",
            Name = "Active Holder Inc.",
            Description = "Keeps the ticker",
        };
        var updating = new CommonStock
        {
            Cik = "0001067983",
            Ticker = "OLD",
            Name = "Old Name Inc.",
            Description = "Wants NEW but is blocked",
        };
        DbContext.AddRange(activeHolder, updating);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .GetActiveCompanies()
            .Returns(
                new List<CompanyInfo>
                {
                    // Updating company processed first, then the active holder
                    // (whose payload is identical to its stored row).
                    new()
                    {
                        Cik = "0001067983",
                        Name = "Berkshire Hathaway Inc.",
                        Tickers = ["NEW"],
                        EntityType = "operating",
                    },
                    new()
                    {
                        Cik = "0000000001",
                        Name = "Active Holder Inc.",
                        Tickers = ["NEW"],
                        EntityType = "operating",
                    },
                }
            );

        await BuildService(secEdgarClient).SyncCompaniesFromSecApi();

        await using var verify = Fixture.CreateDbContext();
        var stocks = await verify
            .Set<CommonStock>()
            .AsNoTracking()
            .OrderBy(s => s.Cik)
            .ToListAsync();
        stocks.Should().HaveCount(2, "neither row may be deleted");

        var holder = stocks.Single(s => s.Cik == "0000000001");
        holder.Ticker.Should().Be("NEW");
        holder.Name.Should().Be("Active Holder Inc.");

        var blocked = stocks.Single(s => s.Cik == "0001067983");
        blocked.Ticker.Should().Be("OLD", "the update is skipped — ticker is in active use");
        blocked.Name.Should().Be("Old Name Inc.");
    }
}
