using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Yahoo.Data.Models;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins the filed-type override end to end: SEC lists HeartBeam's warrant first, the issuer's
/// own 12(b) rows name BEAT common stock, so the sync re-presents the issuer by its existing BEAT
/// listing and keeps the warrant as a directory-listed sibling.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CompanySyncServicePresentationChoiceTests : ParadeDbMcpTestBase
{
    public CompanySyncServicePresentationChoiceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task FiledWarrantPresentation_MovesToTheExistingFiledCommonListing()
    {
        var existing = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: "0001779372",
            Ticker: "BEATW",
            Name: "Heartbeam, Inc.",
            SecondaryTickers: ["BEAT"]
        );
        DbContext.Add(existing);
        DbContext.AddRange(
            Registration(existing.Id, "BEATW", "Warrant"),
            Registration(existing.Id, "BEAT", "Common Stock")
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var beatListingId = await DbContext
            .Set<EquityListing>()
            .Where(listing => listing.Ticker == "BEAT")
            .Select(listing => listing.Id)
            .SingleAsync();

        await BuildSut(["BEATW", "BEAT"]).SyncCompaniesFromSecApi();

        await using var verify = Fixture.CreateDbContext();
        var issuer = await verify.Set<EquityIssuer>().AsNoTracking().SingleAsync();
        issuer.Presentation.EquityListingId.Should().Be(beatListingId);
        issuer.Presentation.Listing.Ticker.Should().Be("BEAT");
        issuer
            .Securities.SelectMany(security => security.Listings)
            .Should()
            .ContainSingle(listing => listing.Ticker == "BEATW")
            .Which.IsDirectoryListed.Should()
            .BeTrue();
        issuer.Securities.Should().HaveCount(2, "the flip must reuse the existing BEAT security");
    }

    [Fact]
    public async Task UnclassifiedFirstTicker_KeepsSecOrder()
    {
        var existing = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: "0001562733",
            Ticker: "SNYRQ",
            Name: "Synchronoss",
            SecondaryTickers: ["SNYR"]
        );
        DbContext.Add(existing);
        DbContext.Add(Registration(existing.Id, "SNYR", "Common Stock"));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await BuildSut(["SNYRQ", "SNYR"]).SyncCompaniesFromSecApi();

        await using var verify = Fixture.CreateDbContext();
        var issuer = await verify.Set<EquityIssuer>().AsNoTracking().SingleAsync();
        issuer.Presentation.Listing.Ticker.Should().Be("SNYRQ");
    }

    // SEC listed Texas Capital's new TXCP ahead of TCBI days before any price existed for it.
    [Fact]
    public async Task NewFirstSymbolWithoutPrices_WaitsForItsFirstPriceBeforePresenting()
    {
        var existing = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: "0001077428",
            Ticker: "TCBI",
            Name: "Texas Capital Bancshares Inc"
        );
        DbContext.Add(existing);
        DbContext.Add(Bar(existing.Presentation.EquityListingId, "TCBI", daysAgo: 2));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await BuildSut(["TXCP", "TCBI"], "0001077428").SyncCompaniesFromSecApi();

        Guid newListingId;
        await using (var held = Fixture.CreateDbContext())
        {
            var issuer = await held.Set<EquityIssuer>().AsNoTracking().SingleAsync();
            issuer.Presentation.Listing.Ticker.Should().Be("TCBI");
            var added = issuer
                .Securities.SelectMany(security => security.Listings)
                .Should()
                .ContainSingle(listing => listing.Ticker == "TXCP")
                .Which;
            added.IsDirectoryListed.Should().BeTrue("the new symbol must still be priced");
            newListingId = added.Id;
        }

        DbContext.ChangeTracker.Clear();
        DbContext.Add(Bar(newListingId, "TXCP", daysAgo: 1));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        await BuildSut(["TXCP", "TCBI"], "0001077428").SyncCompaniesFromSecApi();

        await using var moved = Fixture.CreateDbContext();
        (await moved.Set<EquityIssuer>().AsNoTracking().SingleAsync())
            .Presentation.Listing.Ticker.Should()
            .Be("TXCP");
    }

    [Fact]
    public async Task PresentingLineWithNoRecentBar_YieldsToTheNewFirstSymbol()
    {
        var existing = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: "0001077428",
            Ticker: "TCBI",
            Name: "Texas Capital Bancshares Inc"
        );
        DbContext.Add(existing);
        DbContext.Add(Bar(existing.Presentation.EquityListingId, "TCBI", daysAgo: 40));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await BuildSut(["TXCP", "TCBI"], "0001077428").SyncCompaniesFromSecApi();

        await using var verify = Fixture.CreateDbContext();
        (await verify.Set<EquityIssuer>().AsNoTracking().SingleAsync())
            .Presentation.Listing.Ticker.Should()
            .Be("TXCP");
    }

    private static EquityDailyStockPrice Bar(Guid listingId, string ticker, int daysAgo) =>
        new()
        {
            EquityListingId = listingId,
            SourceTicker = ticker,
            Date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-daysAgo),
            Open = 10m,
            High = 10m,
            Low = 10m,
            Close = 10m,
            AdjustedClose = 10m,
            Volume = 1_000,
        };

    private static IssuerSecurityRegistration Registration(
        Guid issuerId,
        string symbol,
        string title
    ) =>
        new()
        {
            EquityIssuerId = issuerId,
            TradingSymbol = symbol,
            Title = title,
            AccessionNumber = "0001779372-26-000001",
            FiledDate = new DateOnly(2026, 8, 14),
        };

    private CompanySyncService BuildSut(List<string> tickers, string cik = null)
    {
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .GetActiveCompanies()
            .Returns(
                new List<CompanyInfo>
                {
                    new()
                    {
                        Cik = cik ?? (tickers[0] == "BEATW" ? "0001779372" : "0001562733"),
                        Name = "Issuer",
                        Tickers = tickers,
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
}
