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
/// Pins the three remaining orchestration skip branches of
/// <c>SyncCompaniesFromSecApi</c> in one crafted sweep: a subsidiary CIK
/// attached to two parents (duplicate-parent warning), an incoming CIK that is
/// already a known subsidiary (subsidiary-skip), and an incoming company with
/// no tickers (no-ticker skip). None must create or mutate a row.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CompanySyncServiceOrchestrationSkipTests : ParadeDbMcpTestBase
{
    public CompanySyncServiceOrchestrationSkipTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SyncCompaniesFromSecApi_DuplicateSubParentAndSubsidiaryAndNoTicker_AllSkipped()
    {
        // Both stocks list subsidiary CIK 0000000099 → the second TryAdd fails
        // and logs the duplicate-parent warning.
        var stockA = new CommonStock
        {
            Cik = "0000000001",
            Ticker = "AAA",
            Name = "Alpha Inc.",
            SecondaryCiks = ["0000000099"],
        };
        var stockB = new CommonStock
        {
            Cik = "0000000002",
            Ticker = "BBB",
            Name = "Beta Inc.",
            SecondaryCiks = ["0000000099", "0000000088"],
        };
        DbContext.AddRange(stockA, stockB);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .GetActiveCompanies()
            .Returns(
                new List<CompanyInfo>
                {
                    // Known subsidiary (in SecondaryCikToParent) → subsidiary-skip.
                    new()
                    {
                        Cik = "0000000099",
                        Name = "Subsidiary Co",
                        Tickers = ["SUB"],
                        EntityType = "operating",
                    },
                    // No tickers → no-ticker skip.
                    new()
                    {
                        Cik = "0000000077",
                        Name = "Tickerless Co",
                        Tickers = [],
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
        stocks.Should().HaveCount(2, "every incoming company was skipped — no rows created");
        stocks.Select(s => s.Cik).Should().BeEquivalentTo(["0000000001", "0000000002"]);
    }
}
