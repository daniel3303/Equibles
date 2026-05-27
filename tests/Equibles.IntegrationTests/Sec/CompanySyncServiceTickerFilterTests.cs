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
/// Sibling to the Create/Update/Replace/Collision CompanySyncService pins, which
/// all run with an empty TickersToSync. This pins the WorkerOptions.TickersToSync
/// filter branch (zero-hit): when an operator restricts the sync to specific
/// tickers, companies whose tickers are all outside that allow-list must be
/// dropped before any DB work. A regression to that LINQ filter would either
/// sync the entire SEC universe (massive unwanted load) or nothing at all.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CompanySyncServiceTickerFilterTests : ParadeDbMcpTestBase
{
    public CompanySyncServiceTickerFilterTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SyncCompaniesFromSecApi_TickersToSyncConfigured_OnlyPersistsAllowedCompanies()
    {
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .GetActiveCompanies()
            .Returns(
                new List<CompanyInfo>
                {
                    new()
                    {
                        Cik = "0000320193",
                        Name = "Apple Inc.",
                        Tickers = ["AAPL"],
                        EntityType = "operating",
                    },
                    new()
                    {
                        Cik = "0000789019",
                        Name = "Microsoft Corp",
                        Tickers = ["MSFT"],
                        EntityType = "operating",
                    },
                }
            );

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(CommonStockRepository), new CommonStockRepository(DbContext)),
            (
                typeof(CommonStockManager),
                new CommonStockManager(new CommonStockRepository(DbContext), Substitute.For<IBus>())
            ),
            (typeof(EquiblesFinancialDbContext), DbContext)
        );

        var sut = new CompanySyncService(
            scopeFactory,
            secEdgarClient,
            Options.Create(new WorkerOptions { TickersToSync = ["AAPL"] }),
            Substitute.For<ILogger<CompanySyncService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        await sut.SyncCompaniesFromSecApi();

        // Only AAPL survives the TickersToSync filter — MSFT is dropped before
        // any create, so the DB holds exactly the one allow-listed company.
        await using var verify = Fixture.CreateDbContext();
        var stocks = await verify.Set<CommonStock>().AsNoTracking().ToListAsync();
        stocks.Should().ContainSingle();
        stocks[0].Ticker.Should().Be("AAPL");
    }
}
