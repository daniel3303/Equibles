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
/// Sibling to <see cref="CompanySyncServiceTests"/> which pins the CreateNewStock
/// branch. This pins UpdateExistingStock: a SEC company whose CIK already exists
/// in the DB with a different primary ticker must update in-place — the CIK is
/// the immutable identifier; the ticker can change (e.g., a corporate ticker
/// reassignment). A regression that created a new row instead of updating would
/// produce duplicate CIK records and break the unique-by-CIK invariant.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CompanySyncServiceUpdateExistingTests : ParadeDbMcpTestBase
{
    public CompanySyncServiceUpdateExistingTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SyncCompaniesFromSecApi_ExistingCikWithDifferentTicker_UpdatesInPlace()
    {
        // Seed an existing stock whose CIK appears in the next SEC sync payload.
        EquityIssuer existing = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: "0001067983",
            Ticker: "OLD",
            Name: "Old Name Inc.",
            SecondaryTickers: ["X.A", "FUND-X"],
            ReferenceTickers: ["FUND-X"],
            Description: "Pre-sync description"
        );
        DbContext.Add(existing);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

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
                        Tickers = ["BRK.A", "BRK.B"],
                        EntityType = "operating",
                    },
                }
            );

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

        var sut = new CompanySyncService(
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

        await sut.SyncCompaniesFromSecApi();

        // Re-read from a fresh context — the row must be the SAME row (same Id)
        // with updated ticker/name/secondaries, NOT a new duplicate.
        await using var verify = Fixture.CreateDbContext();
        var stocks = await verify.Set<EquityIssuer>().AsNoTracking().ToListAsync();
        stocks.Should().ContainSingle("CIK must be unique — never duplicated");
        stocks[0].Id.Should().Be(existing.Id);
        stocks[0].Presentation.Listing.Ticker.Should().Be("BRK.A");
        stocks[0].Name.Should().Be("Berkshire Hathaway Inc.");
        stocks[0]
            .Securities.SelectMany(nativeSecurity => nativeSecurity.Listings)
            .Where(nativeListing =>
                nativeListing.MarketCountryCode == "US" && (nativeListing.IsReferenceListed)
            )
            .Select(nativeListing => nativeListing.Ticker)
            .ToList()
            .Should()
            .BeEquivalentTo("FUND-X");
        stocks[0]
            .Securities.SelectMany(nativeSecurity => nativeSecurity.Listings)
            .Where(nativeListing =>
                nativeListing.MarketCountryCode == "US"
                && (
                    nativeListing.IsDirectoryListed
                    && nativeListing.Id != stocks[0].Presentation.EquityListingId
                )
            )
            .Select(nativeListing => nativeListing.Ticker)
            .ToList()
            .Should()
            .BeEquivalentTo("BRK.B", "FUND-X");
    }

    [Fact]
    public async Task SyncCompaniesFromSecApi_EquivalentCikSpellingPreservesReferenceOwner()
    {
        EquityIssuer existing = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: "0000036405",
            Ticker: "VOO",
            Name: "Vanguard S&P 500 ETF",
            SecondaryTickers: ["VOO"],
            ReferenceTickers: ["VOO"]
        );
        DbContext.Add(existing);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .GetActiveCompanies()
            .Returns([
                new CompanyInfo
                {
                    Cik = "36405",
                    Name = "Vanguard S&P 500 ETF",
                    Tickers = ["VOO"],
                    EntityType = "operating",
                },
            ]);

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
        var sut = new CompanySyncService(
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

        await sut.SyncCompaniesFromSecApi();

        await using var verify = Fixture.CreateDbContext();
        var stocks = await verify.Set<EquityIssuer>().AsNoTracking().ToListAsync();
        stocks.Should().ContainSingle();
        stocks[0].Id.Should().Be(existing.Id);
        stocks[0].Cik.Should().Be("0000036405");
        stocks[0]
            .Securities.SelectMany(nativeSecurity => nativeSecurity.Listings)
            .Where(nativeListing =>
                nativeListing.MarketCountryCode == "US" && (nativeListing.IsReferenceListed)
            )
            .Select(nativeListing => nativeListing.Ticker)
            .ToList()
            .Should()
            .BeEquivalentTo("VOO");
    }

    [Fact]
    public async Task SyncCompaniesFromSecApi_RenameCollidesWithReferenceOwner_PreservesBothRows()
    {
        EquityIssuer referenceOwner = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: "999",
            Ticker: "REUSED",
            Name: "Reference-owned ETF",
            ReferenceTickers: ["REUSED"]
        );
        EquityIssuer incoming = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: "111",
            Ticker: "OLD",
            Name: "Incoming SEC company"
        );
        DbContext.AddRange(referenceOwner, incoming);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .GetActiveCompanies()
            .Returns([
                new CompanyInfo
                {
                    Cik = "111",
                    Name = "Incoming SEC company",
                    Tickers = ["REUSED"],
                    EntityType = "operating",
                },
            ]);

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
        var sut = new CompanySyncService(
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

        await sut.SyncCompaniesFromSecApi();

        await using var verify = Fixture.CreateDbContext();
        var stocks = await verify.Set<EquityIssuer>().AsNoTracking().ToListAsync();
        stocks.Should().HaveCount(2);
        stocks
            .Single(stock => stock.Id == referenceOwner.Id)
            .Presentation.Listing.Ticker.Should()
            .Be("REUSED");
        stocks
            .Single(stock => stock.Id == incoming.Id)
            .Presentation.Listing.Ticker.Should()
            .Be("OLD");
    }
}
