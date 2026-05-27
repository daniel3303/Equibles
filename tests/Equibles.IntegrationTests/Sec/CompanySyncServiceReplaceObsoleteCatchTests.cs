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
/// Completes the <c>ReplaceObsoleteStock</c> pins: the replace path is exercised
/// (obsolete row deleted) but the replacement <c>CommonStockManager.Create</c>
/// fails validation (empty Name) and the catch arm must log and report rather
/// than rethrow — so one malformed SEC entry can't abort the sync after the
/// obsolete delete already committed.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CompanySyncServiceReplaceObsoleteCatchTests : ParadeDbMcpTestBase
{
    public CompanySyncServiceReplaceObsoleteCatchTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SyncCompaniesFromSecApi_ReplaceCreateFailsValidation_DeletesObsoleteThenReportsError()
    {
        var obsolete = new CommonStock
        {
            Cik = "0000000999",
            Ticker = "REUSED",
            Name = "Defunct Old Inc.",
        };
        DbContext.Add(obsolete);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        // New CIK (not in feed-scoped existing set) wants REUSED. The obsolete
        // holder's own CIK is absent from the feed → replace path. The new
        // company has an empty Name → Create throws after the obsolete delete
        // has already committed, exercising the catch arm.
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
                        Tickers = ["REUSED"],
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

        await using var verify = Fixture.CreateDbContext();
        var stocks = await verify.Set<CommonStock>().AsNoTracking().ToListAsync();
        stocks
            .Should()
            .BeEmpty("the obsolete row was deleted and the invalid replacement was not created");
    }
}
