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
/// Comprehensive pin of CompanySyncService's create/replace/collision dispatch.
/// The existing tests only cover the no-collision update and the ticker-filter;
/// this covers the four remaining routing outcomes (CreateNewStock,
/// ReplaceObsoleteStock delete+recreate, ResolveTickerCollision incumbent-wins,
/// ResolveTickerCollision incoming-wins) — all zero-hit. Each protects a distinct
/// data-integrity rule of the SEC company sync.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CompanySyncServiceDispatchTests : ParadeDbMcpTestBase
{
    public CompanySyncServiceDispatchTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private CompanySyncService BuildSut(ISecEdgarClient secEdgarClient)
    {
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(CommonStockRepository), new CommonStockRepository(DbContext)),
            (
                typeof(CommonStockManager),
                new CommonStockManager(new CommonStockRepository(DbContext))
            ),
            (typeof(EquiblesDbContext), DbContext)
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

    private static CompanyInfo Company(string cik, string ticker, string name) =>
        new()
        {
            Cik = cik,
            Name = name,
            Tickers = [ticker],
            EntityType = "operating",
        };

    [Fact]
    public async Task SyncCompaniesFromSecApi_BrandNewCikAndTicker_CreatesStock()
    {
        var client = Substitute.For<ISecEdgarClient>();
        client
            .GetActiveCompanies()
            .Returns(new List<CompanyInfo> { Company("0000000010", "NEW", "New Co") });

        await BuildSut(client).SyncCompaniesFromSecApi();

        await using var verify = Fixture.CreateDbContext();
        var stocks = await verify.Set<CommonStock>().AsNoTracking().ToListAsync();
        stocks.Should().ContainSingle();
        stocks[0].Cik.Should().Be("0000000010");
        stocks[0].Ticker.Should().Be("NEW");
    }

    [Fact]
    public async Task SyncCompaniesFromSecApi_TickerHeldByCompanyNotInFeed_ReplacesObsoleteStock()
    {
        DbContext.Add(
            new CommonStock
            {
                Cik = "0000000020",
                Ticker = "REPL",
                Name = "Old Holder",
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var client = Substitute.For<ISecEdgarClient>();
        client
            .GetActiveCompanies()
            .Returns(new List<CompanyInfo> { Company("0000000021", "REPL", "New Holder") });

        await BuildSut(client).SyncCompaniesFromSecApi();

        // Old holder (CIK not in feed) deleted; the incoming CIK now owns REPL.
        await using var verify = Fixture.CreateDbContext();
        var stocks = await verify.Set<CommonStock>().AsNoTracking().ToListAsync();
        stocks.Should().ContainSingle();
        stocks[0].Cik.Should().Be("0000000021");
        stocks[0].Ticker.Should().Be("REPL");
    }

    [Fact]
    public async Task SyncCompaniesFromSecApi_TwoActiveCiksSameTickerMetadataMissing_AttachesIncomingAsSubsidiary()
    {
        DbContext.Add(
            new CommonStock
            {
                Cik = "0000000030",
                Ticker = "DUAL",
                Name = "Incumbent",
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var client = Substitute.For<ISecEdgarClient>();
        client
            .GetActiveCompanies()
            .Returns(
                new List<CompanyInfo>
                {
                    Company("0000000030", "DUAL", "Incumbent"),
                    Company("0000000031", "DUAL", "Incoming"),
                }
            );
        // GetCompanyMetadata unstubbed → null → ShouldIncumbentWin returns true.

        await BuildSut(client).SyncCompaniesFromSecApi();

        await using var verify = Fixture.CreateDbContext();
        var incumbent = await verify
            .Set<CommonStock>()
            .AsNoTracking()
            .SingleAsync(s => s.Cik == "0000000030");
        incumbent.SecondaryCiks.Should().Contain("0000000031");
        (await verify.Set<CommonStock>().AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SyncCompaniesFromSecApi_IncomingListedIncumbentNot_LogsManualReviewWithoutSwap()
    {
        DbContext.Add(
            new CommonStock
            {
                Cik = "0000000040",
                Ticker = "DUAL",
                Name = "Incumbent",
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var client = Substitute.For<ISecEdgarClient>();
        client
            .GetActiveCompanies()
            .Returns(
                new List<CompanyInfo>
                {
                    Company("0000000040", "DUAL", "Incumbent"),
                    Company("0000000041", "DUAL", "Incoming"),
                }
            );
        client
            .GetCompanyMetadata("0000000041")
            .Returns(
                new CompanyMetadata
                {
                    Cik = "0000000041",
                    EntityType = "operating",
                    Exchanges = ["NASDAQ"],
                }
            );
        client
            .GetCompanyMetadata("0000000040")
            .Returns(
                new CompanyMetadata
                {
                    Cik = "0000000040",
                    EntityType = "operating",
                    Exchanges = [],
                }
            );

        await BuildSut(client).SyncCompaniesFromSecApi();

        // Incoming is the listed entity → not auto-swapped: incumbent keeps the
        // ticker, no subsidiary attached, no new row.
        await using var verify = Fixture.CreateDbContext();
        var incumbent = await verify
            .Set<CommonStock>()
            .AsNoTracking()
            .SingleAsync(s => s.Cik == "0000000040");
        incumbent.SecondaryCiks.Should().BeEmpty();
        (await verify.Set<CommonStock>().AsNoTracking().CountAsync()).Should().Be(1);
    }
}
