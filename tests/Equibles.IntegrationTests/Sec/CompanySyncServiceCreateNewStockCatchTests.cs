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
/// <see cref="CompanySyncServiceTests"/> pins the CreateNewStock success path.
/// This pins its catch arm: a SEC company with a fresh CIK and ticker (so it
/// routes to CreateNewStock) but a missing Name makes
/// <c>CommonStockManager.Create</c> throw <c>DomainValidationException</c> before
/// any write. The sync must log, report the error, and continue — one malformed
/// SEC entry can never abort the whole company sync or leave a half-written row.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CompanySyncServiceCreateNewStockCatchTests : ParadeDbMcpTestBase
{
    public CompanySyncServiceCreateNewStockCatchTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SyncCompaniesFromSecApi_NewCompanyWithMissingName_ReportsErrorAndCreatesNoRow()
    {
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .GetActiveCompanies()
            .Returns(
                new List<CompanyInfo>
                {
                    new()
                    {
                        Cik = "0000000111",
                        Name = "",
                        Tickers = ["NEWT"],
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

        var errorScopeFactory = Substitute.For<IServiceScopeFactory>();
        var sut = new CompanySyncService(
            scopeFactory,
            secEdgarClient,
            Options.Create(new WorkerOptions { TickersToSync = [] }),
            Substitute.For<ILogger<CompanySyncService>>(),
            new ErrorReporter(errorScopeFactory, Substitute.For<ILogger<ErrorReporter>>())
        );

        // The whole sync must complete (the bad entry is caught, not rethrown).
        await sut.SyncCompaniesFromSecApi();

        await using var verify = Fixture.CreateDbContext();
        var stocks = await verify.Set<CommonStock>().AsNoTracking().ToListAsync();
        stocks.Should().BeEmpty("the invalid company must not produce a row");
    }
}
