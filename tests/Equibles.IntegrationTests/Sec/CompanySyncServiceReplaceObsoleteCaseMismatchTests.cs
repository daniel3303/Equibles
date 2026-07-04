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
/// <see cref="CompanySyncServiceReplaceObsoleteTests"/> pins the replace path
/// (obsolete row deleted, new one created). This pins that the path also fires
/// when the incoming ticker differs from the stored one only by CASE: the
/// routing guard <c>ExistingPrimaryTickers</c> is <c>OrdinalIgnoreCase</c>, and
/// <c>PrimaryTickerToStock</c> must match it — a case-SENSITIVE lookup routed
/// the company into <c>ReplaceObsoleteStock</c> but then failed to resolve the
/// ticker holder, so the incoming company was skipped on every sync cycle
/// forever (and an exact-duplicate ticker would have thrown and aborted the
/// whole cycle).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CompanySyncServiceReplaceObsoleteCaseMismatchTests : ParadeDbMcpTestBase
{
    public CompanySyncServiceReplaceObsoleteCaseMismatchTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SyncCompaniesFromSecApi_TickerCaseMismatch_ResolvesHolderAndReplacesObsoleteStock()
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
        // to ReplaceObsoleteStock; the case-insensitive PrimaryTickerToStock
        // resolves the stored holder so the replace proceeds.
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
        stocks
            .Should()
            .ContainSingle("the obsolete holder must be replaced, not kept alongside a duplicate");
        stocks[0].Cik.Should().Be("0000000111");
        stocks[0].Ticker.Should().Be("reused");
        stocks[0].Name.Should().Be("Acquirer Inc.");
    }
}
