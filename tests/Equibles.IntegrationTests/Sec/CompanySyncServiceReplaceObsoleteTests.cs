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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins <c>ReplaceObsoleteStock</c> — the third major branch of
/// <see cref="CompanySyncService.SyncCompaniesFromSecApi"/>, alongside the
/// sibling tests for <c>CreateNewStock</c> and <c>UpdateExistingStock</c>.
/// When an incoming SEC company has a NEW CIK but its primary ticker is already
/// held by another stock whose CIK has DROPPED from SEC's feed, the existing
/// stock is obsolete and must be removed; the new one takes its ticker. A
/// regression that skipped the delete would trigger a unique-ticker constraint
/// failure on Create, breaking every sync where a ticker reassignment ever
/// happens.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CompanySyncServiceReplaceObsoleteTests : ParadeDbMcpTestBase
{
    public CompanySyncServiceReplaceObsoleteTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SyncCompaniesFromSecApi_NewCikReusesTickerOfDroppedCik_ReplacesObsoleteRow()
    {
        // Seed an "obsolete" stock whose CIK no longer appears in SEC's feed.
        var obsolete = new CommonStock
        {
            Cik = "0000000999",
            Ticker = "REUSED",
            Name = "Defunct Old Inc.",
        };
        DbContext.Add(obsolete);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        // SEC returns ONE company: a brand-new CIK that wants the REUSED ticker.
        // The obsolete CIK is not in the feed → its row is removed; the new one
        // takes the ticker.
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .GetActiveCompanies()
            .Returns(
                new List<CompanyInfo>
                {
                    new()
                    {
                        Cik = "0000000111",
                        Name = "Acquirer Inc.",
                        Tickers = ["REUSED"],
                        EntityType = "operating",
                    },
                }
            );

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

        await using var verify = Fixture.CreateDbContext();
        var stocks = await verify.Set<CommonStock>().AsNoTracking().ToListAsync();
        stocks.Should().ContainSingle("the obsolete CIK is gone and the new one took its ticker");
        stocks[0].Cik.Should().Be("0000000111");
        stocks[0].Ticker.Should().Be("REUSED");
        stocks[0].Name.Should().Be("Acquirer Inc.");
    }
}
