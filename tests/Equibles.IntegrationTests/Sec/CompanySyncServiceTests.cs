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
/// The unit-tier <c>CompanySyncServiceTests</c> in <c>Equibles.UnitTests.Sec</c> explicitly
/// skips the public <see cref="CompanySyncService.SyncCompaniesFromSecApi"/> entry path
/// because it depends on PostgreSQL-specific column semantics (the <c>List&lt;string&gt;</c>
/// columns <c>SecondaryTickers</c> / <c>SecondaryCiks</c> map to a Postgres <c>text[]</c>
/// which EF Core's InMemory provider can't represent faithfully). With a real ParadeDB
/// container this test pins the <c>CreateNewStock</c> branch end-to-end: a SEC company
/// with a primary ticker plus several secondary tickers must round-trip through
/// <see cref="CommonStockManager"/> validation and survive a re-read against Postgres
/// with the secondary tickers intact and ordered.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CompanySyncServiceTests : ParadeDbMcpTestBase
{
    public CompanySyncServiceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SyncCompaniesFromSecApi_EmptyDatabase_CreatesNewStockWithSecondaryTickersPersisted()
    {
        // Three tickers — primary "BRK.A", two secondaries — exercise the path that the
        // unit suite can't reach: the foreach over SecondaryTickers in CreateNewStock and
        // the EF mapping to a Postgres text[] column. A regression that flipped the
        // ordering, dropped entries, or broke the text[] mapping would fail the final
        // assertion after re-reading from a fresh DbContext (no tracker reuse).
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
                        Tickers = ["BRK.A", "BRK.B", "BRK"],
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

        // Re-read from a brand-new context so the assertion exercises the persisted
        // row, not the change-tracker copy. This catches text[] mapping regressions
        // that EF Core's InMemory provider silently glosses over.
        await using var verify = Fixture.CreateDbContext();
        var stocks = await verify.Set<CommonStock>().AsNoTracking().ToListAsync();

        stocks.Should().ContainSingle();
        var stock = stocks[0];
        stock.Cik.Should().Be("0001067983");
        stock.Ticker.Should().Be("BRK.A");
        stock.Name.Should().Be("Berkshire Hathaway Inc.");
        stock.SecondaryTickers.Should().Equal("BRK.B", "BRK");
    }
}
