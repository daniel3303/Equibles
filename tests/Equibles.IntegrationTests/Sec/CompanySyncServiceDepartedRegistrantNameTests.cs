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
/// A registrant SEC no longer lists never reaches the per-company update, so the sync drops the
/// filing suffixes from its stored name on its own pass, keeping the casing another source gave it.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CompanySyncServiceDepartedRegistrantNameTests : ParadeDbMcpTestBase
{
    public CompanySyncServiceDepartedRegistrantNameTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private CompanySyncService BuildSut(ISecEdgarClient secEdgarClient)
    {
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(EquityIssuerRepository), new EquityIssuerRepository(DbContext)),
            (
                typeof(EquityIdentityManager),
                new EquityIdentityManager(
                    new EquityIssuerRepository(DbContext),
                    Substitute.For<IBus>()
                )
            ),
            (typeof(EquiblesFinancialDbContext), DbContext)
        );
        return new CompanySyncService(
            scopeFactory,
            secEdgarClient,
            Options.Create(new WorkerOptions { TickersToSync = [] }),
            Substitute.For<ILogger<CompanySyncService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Substitute.For<IBus>()
        );
    }

    [Fact]
    public async Task SyncCompaniesFromSecApi_DropsFilingSuffixesFromRegistrantsSecNoLongerLists()
    {
        DbContext.AddRange(
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Cik: "0000000040",
                Ticker: "ADRF",
                Name: "Airbus SE/ADR"
            ),
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Cik: "0000000041",
                Ticker: "RAIL",
                Name: "EAST JAPAN RAILWAY CO /FI"
            ),
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Cik: "0000000042",
                Ticker: "LIST",
                Name: "Listed Corp /De/"
            ),
            Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "VENU", Name: "Venue Co/ADR")
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var client = Substitute.For<ISecEdgarClient>();
        client
            .GetActiveCompanies()
            .Returns(
                new List<CompanyInfo>
                {
                    new()
                    {
                        Cik = "0000000042",
                        Name = "LISTED CORP /DE/",
                        Tickers = ["LIST"],
                        EntityType = "operating",
                    },
                }
            );

        await BuildSut(client).SyncCompaniesFromSecApi();

        await using var verify = Fixture.CreateDbContext();
        var names = await verify
            .Set<EquityIssuer>()
            .AsNoTracking()
            .ToDictionaryAsync(issuer => issuer.Cik ?? "none", issuer => issuer.Name);
        names["0000000040"].Should().Be("Airbus SE");
        names["0000000041"].Should().Be("EAST JAPAN RAILWAY CO");
        names["0000000042"].Should().Be("Listed Corp");
        // An issuer with no CIK was never named by SEC.
        names["none"].Should().Be("Venue Co/ADR");
    }
}
