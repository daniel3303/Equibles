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
/// Pins <c>UpdateExistingStock</c>'s rollback catch arm. An existing CIK whose
/// SEC payload changes a field (so needsUpdate is true) but carries an empty
/// Name makes <c>CommonStockManager.Update</c> throw <c>DomainValidationException</c>
/// during validation, before SaveChanges. The catch must revert the in-memory
/// entity to its old values and detach it so the dirty state is never persisted
/// by a later SaveChanges in the same sync — the stored row stays exactly as it
/// was, and the sync completes.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CompanySyncServiceUpdateExistingRollbackTests : ParadeDbMcpTestBase
{
    public CompanySyncServiceUpdateExistingRollbackTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SyncCompaniesFromSecApi_ExistingCikUpdateFailsValidation_RollsBackAndKeepsStoredRow()
    {
        var stored = new CommonStock
        {
            Cik = "0000000999",
            Ticker = "KEEP",
            Name = "Original Name Inc.",
            SecondaryTickers = ["KEEP.A"],
        };
        DbContext.Add(stored);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        // Same CIK and primary ticker (no collision branch), but a changed
        // payload (empty Name) → needsUpdate is true and Update throws on the
        // "Name is required" validation.
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .GetActiveCompanies()
            .Returns(
                new List<CompanyInfo>
                {
                    new()
                    {
                        Cik = "0000000999",
                        Name = "",
                        Tickers = ["KEEP"],
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
        stocks.Should().ContainSingle("the failed update must not delete or duplicate the row");
        stocks[0].Cik.Should().Be("0000000999");
        stocks[0].Ticker.Should().Be("KEEP");
        stocks[0]
            .Name.Should()
            .Be("Original Name Inc.", "the empty Name must have been rolled back, not persisted");
    }
}
