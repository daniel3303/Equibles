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
/// <see cref="CompanySyncServiceReplaceObsoleteTests"/> pins the replace path
/// (obsolete row deleted, new one created). This pins the defensive
/// holder-could-not-be-loaded arm of <c>ReplaceObsoleteStock</c>: the routing
/// guard <c>ExistingPrimaryTickers</c> is <c>OrdinalIgnoreCase</c> while
/// <c>PrimaryTickerToStock</c> is a case-sensitive dictionary, so a SEC payload
/// whose ticker differs only in case from a stored ticker is routed into
/// <c>ReplaceObsoleteStock</c> but fails the dictionary lookup. The method must
/// log and skip — never delete the stored row or create a duplicate — leaving
/// the database untouched.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CompanySyncServiceReplaceObsoleteHolderNullTests : ParadeDbMcpTestBase
{
    public CompanySyncServiceReplaceObsoleteHolderNullTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SyncCompaniesFromSecApi_TickerCaseMismatchHolderNotResolvable_LogsAndLeavesDbUntouched()
    {
        var stored = new CommonStock
        {
            Cik = "0000000999",
            Ticker = "REUSED",
            Name = "Stored Stock Inc.",
        };
        DbContext.Add(stored);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        // New CIK (not in DB) whose ticker matches "REUSED" only case-
        // insensitively. ExistingPrimaryTickers (OrdinalIgnoreCase) routes this
        // to ReplaceObsoleteStock; PrimaryTickerToStock (case-sensitive, keyed
        // "REUSED") then can't resolve "reused" → obsoleteStock == null.
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
                        Tickers = ["reused"],
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
        stocks.Should().ContainSingle("the stored row must be neither deleted nor duplicated");
        stocks[0].Cik.Should().Be("0000000999");
        stocks[0].Ticker.Should().Be("REUSED");
        stocks[0].Name.Should().Be("Stored Stock Inc.");
    }
}
