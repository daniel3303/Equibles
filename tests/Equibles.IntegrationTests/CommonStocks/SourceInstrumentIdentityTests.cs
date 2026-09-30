using Equibles.CommonStocks.BusinessLogic.Directory;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class SourceInstrumentIdentityTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    private static EquityDirectoryListingInput Input(
        string security = "share-a",
        string listing = "counter-a",
        string ticker = "ALPHA",
        string currency = "EUR",
        string isin = null,
        string issuer = "issuer-a"
    ) =>
        new()
        {
            Source = "official-directory",
            SourceIssuerIdentifier = issuer,
            IssuerName = "Source company",
            SourceSecurityIdentifier = security,
            SourceListingIdentifier = listing,
            Isin = isin,
            Ticker = ticker,
            MarketIdentifierCode = "XLIS",
            MarketCountryCode = "PT",
            TradingCurrency = currency,
            QuoteUnitMultiplier = 1m,
            SourceUrl = "https://example.org/instruments/" + listing,
            PayloadJson = JsonConvert.SerializeObject(
                new
                {
                    security,
                    listing,
                    ticker,
                    currency,
                    isin,
                    issuer,
                }
            ),
        };

    private ServiceProvider Services() =>
        new ServiceCollection()
            .AddScoped<EquiblesFinancialDbContext>(_ => Fixture.CreateDbContext())
            .AddScoped<EquityIssuerRepository>()
            .AddScoped<EquityListingRepository>()
            .AddScoped<EquityDirectorySourceRecordRepository>()
            .BuildServiceProvider();

    private static EquityDirectoryIdentityImporter Importer(ServiceProvider services) =>
        new(services.GetRequiredService<IServiceScopeFactory>());

    [Fact]
    public async Task MissingIsinPreservesSourceShareClassesWithoutMatchingOtherNullIsins()
    {
        DbContext.Add(
            Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "ALPHA", Name: "Source company")
        );
        await DbContext.SaveChangesAsync();
        await using var services = Services();
        var importer = Importer(services);
        var first = await importer.ImportListing(Input());
        var second = await importer.ImportListing(Input("share-b", "counter-b", "BETA", "USD"));
        (await importer.ImportListing(Input())).Should().Be(first);
        var listings = await DbContext
            .Set<EquityListing>()
            .Include(row => row.Security)
            .Where(row => row.Id == first || row.Id == second)
            .ToListAsync();
        listings.Select(row => row.EquitySecurityId).Distinct().Should().HaveCount(2);
        listings.Select(row => row.Security.EquityIssuerId).Distinct().Should().ContainSingle();
        listings
            .Should()
            .OnlyContain(row =>
                row.Security.Isin == null && row.IdentityState == EquityIdentityState.Verified
            );
        (await DbContext.Set<EquityIssuer>().CountAsync()).Should().Be(2);
        (await DbContext.Set<EquitySecuritySourceIdentifier>().CountAsync()).Should().Be(2);
        (await DbContext.Set<EquityListingSourceIdentifier>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task CurrencyCountersShareSecurityButKeepListingIdentityThroughReplayAndRename()
    {
        await using var services = Services();
        var importer = Importer(services);
        var first = await importer.ImportListing(Input(isin: "PTALT0AE0002"));
        var other = await importer.ImportListing(
            Input(listing: "counter-usd", ticker: "ALPHAUSD", currency: "USD", isin: "PTALT0AE0002")
        );
        first.Should().NotBe(other);
        (await importer.ImportListing(Input(ticker: "NEWALPHA", isin: "PTALT0AE0002")))
            .Should()
            .Be(first);
        (
            await importer.ImportListing(
                Input(
                    listing: "counter-usd",
                    ticker: "ALPHAUSD",
                    currency: "USD",
                    isin: "PTALT0AE0002"
                )
            )
        )
            .Should()
            .Be(other);
        (await DbContext.Set<EquitySecurity>().CountAsync()).Should().Be(1);
        (await DbContext.Set<EquityListing>().CountAsync()).Should().Be(2);
        (await DbContext.Set<EquityListing>().SingleAsync(row => row.Id == first))
            .Ticker.Should()
            .Be("NEWALPHA");
        (await DbContext.Set<EquityListing>().SingleAsync(row => row.Id == other))
            .TradingCurrency.Should()
            .Be("USD");
    }

    [Fact]
    public async Task LaterAuthoritativeIsinEnrichesTheSameSecurity()
    {
        await using var services = Services();
        var importer = Importer(services);
        var original = await importer.ImportListing(Input());
        (await importer.ImportListing(Input(isin: "PTALT0AE0002"))).Should().Be(original);
        (await DbContext.Set<EquitySecurity>().SingleAsync()).Isin.Should().Be("PTALT0AE0002");
        (await DbContext.Set<EquityDirectorySourceRecord>().CountAsync()).Should().Be(2);
        (await DbContext.Set<EquitySecuritySourceIdentifier>().SingleAsync())
            .SourceRecordId.Should()
            .NotBe(Guid.Empty);
    }

    [Theory]
    [InlineData("security")]
    [InlineData("issuer")]
    [InlineData("currency")]
    [InlineData("isin")]
    [InlineData("venue")]
    public async Task ConflictingBindingsRollBackAllIdentityAndEvidenceChanges(string conflict)
    {
        await using var services = Services();
        var importer = Importer(services);
        await importer.ImportListing(Input(isin: "PTALT0AE0002"));
        await importer.ImportListing(
            Input("other-security", "other-listing", "OTHER", issuer: "other-issuer")
        );
        var changed = Input(isin: "PTALT0AE0002");
        switch (conflict)
        {
            case "security":
                changed.SourceSecurityIdentifier = "other-security";
                break;
            case "issuer":
                changed.SourceIssuerIdentifier = "other-issuer";
                break;
            case "currency":
                changed.TradingCurrency = "USD";
                break;
            case "isin":
                changed.Isin = "GB0002875804";
                break;
            case "venue":
                changed.MarketIdentifierCode = "XPAR";
                break;
        }
        changed.PayloadJson = "{\"changed\":true}";
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportListing(changed));
        (await DbContext.Set<EquityIssuer>().CountAsync()).Should().Be(2);
        (await DbContext.Set<EquitySecurity>().CountAsync()).Should().Be(2);
        (await DbContext.Set<EquityListing>().CountAsync()).Should().Be(2);
        (await DbContext.Set<EquityDirectorySourceRecord>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task MissingListingBindingCannotReassignAnActiveTicker()
    {
        await using var services = Services();
        var importer = Importer(services);
        await importer.ImportListing(Input());
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            importer.ImportListing(Input("different-share", "different-counter"))
        );
        (await DbContext.Set<EquitySecurity>().CountAsync()).Should().Be(1);
        (await DbContext.Set<EquityListingSourceIdentifier>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RetiredSourceListingCannotBeSilentlyReactivated()
    {
        await using var services = Services();
        var importer = Importer(services);
        var id = await importer.ImportListing(Input());
        var listing = await DbContext.Set<EquityListing>().SingleAsync(row => row.Id == id);
        listing.Active = false;
        listing.DelistedOn = new(2025, 1, 1);
        await DbContext.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportListing(Input()));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("share", null)]
    [InlineData(null, "counter")]
    [InlineData("", "counter")]
    public async Task MissingIsinRequiresBothAuthoritativeInstrumentIdentifiers(
        string security,
        string listing
    )
    {
        await using var services = Services();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Importer(services).ImportListing(Input(security, listing))
        );
        (await DbContext.Set<EquityIssuer>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public void RollbackCannotDiscardInstrumentIdentityBindings()
    {
        var migration = new Equibles.Migrations.Migrations.AddSourceInstrumentIdentifiers();
        Assert.Throws<NotSupportedException>(() => migration.DownOperations);
    }

    [Fact]
    public async Task ConcurrentSourceReplaysCreateOneIdentityGraph()
    {
        await using var services = Services();
        var ids = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => Importer(services).ImportListing(Input()))
        );
        ids.Distinct().Should().ContainSingle();
        (await DbContext.Set<EquityIssuer>().CountAsync()).Should().Be(1);
        (await DbContext.Set<EquitySecuritySourceIdentifier>().CountAsync()).Should().Be(1);
        (await DbContext.Set<EquityListingSourceIdentifier>().CountAsync()).Should().Be(1);
    }
}
