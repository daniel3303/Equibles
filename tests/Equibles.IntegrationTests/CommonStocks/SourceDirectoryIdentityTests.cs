using System.Text.Json;
using Equibles.CommonStocks.BusinessLogic.Directory;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class SourceDirectoryIdentityTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    private static EquityDirectoryListingInput Input(
        string ticker = "ALTR",
        string mic = "XLIS",
        string currency = "EUR"
    ) =>
        new()
        {
            Source = "euronext",
            SourceIssuerIdentifier = "115374",
            IssuerName = "ALTRI, S.G.P.S., S.A.",
            LegalEntityIdentifier = "213800AKSTYRLHY3X497",
            Isin = "PTALT0AE0002",
            RelatedIsins = ["PTALT0AE0002", "US02209Y1001"],
            Ticker = ticker,
            MarketIdentifierCode = mic,
            MarketCountryCode = "PT",
            TradingCurrency = currency,
            QuoteUnitMultiplier = currency == null ? null : 1m,
            SourceUrl = $"https://live.euronext.com/en/product/equities/PTALT0AE0002-{mic}",
            PayloadJson = JsonSerializer.Serialize(
                new
                {
                    Ticker = ticker,
                    Mic = mic,
                    Currency = currency,
                    Isin = "PTALT0AE0002",
                    IssuerCode = "115374",
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

    [Fact]
    public async Task NewForeignIssuer_DoesNotMergeByNameOrTicker_AndReplayKeepsEveryIdentity()
    {
        var us = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "ALTR",
            Name: Input().IssuerName
        );
        DbContext.Add(us);
        await DbContext.SaveChangesAsync();
        await using var services = Services();
        var importer = new EquityDirectoryIdentityImporter(
            services.GetRequiredService<IServiceScopeFactory>()
        );
        var id = await importer.ImportListing(Input());
        (await importer.ImportListing(Input())).Should().Be(id);
        var foreign = await DbContext
            .Set<EquityListing>()
            .Include(row => row.Security)
                .ThenInclude(row => row.Issuer)
            .SingleAsync(row => row.Id == id);
        foreign.Security.EquityIssuerId.Should().NotBe(us.Id);
        foreign.Security.SecurityType.Should().Be(EquitySecurityKind.Unknown);
        foreign.MarketCountryCode.Should().Be("PT");
        foreign.IdentityState.Should().Be(EquityIdentityState.Verified);
        (
            await new EquityListingRepository(DbContext)
                .GetUsByTicker("ALTR")
                .Select(row => row.Id)
                .SingleAsync()
        )
            .Should()
            .Be(us.Presentation.EquityListingId);
        (await DbContext.Set<EquityIssuerSourceIdentifier>().CountAsync()).Should().Be(1);
        (await DbContext.Set<EquityDirectorySourceRecord>().CountAsync()).Should().Be(1);
        (await DbContext.Set<CommonStock>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SourceIsinToLeiRelationship_SharesTheExistingUsIssuer_ButPreservesDistinctSecuritiesAndPresentation()
    {
        var us = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "ASGSY",
            Name: "Existing issuer profile",
            Cusip: "02209Y100"
        );
        us.Cik = "1234567890";
        us.Description = "Retained description";
        us.Presentation.Listing.Security.SecurityType = EquitySecurityKind.DepositaryReceipt;
        DbContext.Add(us);
        await DbContext.SaveChangesAsync();
        var originalListingId = us.Presentation.EquityListingId;
        var originalSecurityId = us.Presentation.Listing.EquitySecurityId;
        await using var services = Services();
        var importer = new EquityDirectoryIdentityImporter(
            services.GetRequiredService<IServiceScopeFactory>()
        );
        var foreignId = await importer.ImportListing(Input());
        DbContext.ChangeTracker.Clear();
        var issuer = await new EquityIssuerRepository(DbContext).Get(us.Id);
        issuer.Name.Should().Be("Existing issuer profile");
        issuer.Description.Should().Be("Retained description");
        issuer.Cik.Should().Be("1234567890");
        issuer.LegalEntityIdentifier.Should().Be(Input().LegalEntityIdentifier);
        issuer.Presentation.EquityListingId.Should().Be(originalListingId);
        issuer.Securities.Should().HaveCount(2);
        issuer
            .Securities.Single(row => row.Id == originalSecurityId)
            .SecurityType.Should()
            .Be(EquitySecurityKind.DepositaryReceipt);
        var foreign = issuer.Securities.Single(row => row.Id != originalSecurityId);
        foreign.Isin.Should().Be("PTALT0AE0002");
        foreign.SecurityType.Should().Be(EquitySecurityKind.Unknown);
        foreign.Listings.Should().ContainSingle().Which.Id.Should().Be(foreignId);
        (await DbContext.Set<EquityIssuer>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AnotherVenueAndRenamedSymbol_PreserveTheSecurityAndRecordedListingHistory()
    {
        await using var services = Services();
        var importer = new EquityDirectoryIdentityImporter(
            services.GetRequiredService<IServiceScopeFactory>()
        );
        var originalId = await importer.ImportListing(Input());
        var anotherVenue = await importer.ImportListing(Input(mic: "ENXL"));
        var renamed = await importer.ImportListing(Input(ticker: "ALTRNEW"));
        renamed.Should().Be(originalId);
        anotherVenue.Should().NotBe(originalId);
        (await DbContext.Set<EquityIssuer>().CountAsync()).Should().Be(1);
        (await DbContext.Set<EquitySecurity>().CountAsync()).Should().Be(1);
        (
            await DbContext
                .Set<EquityListingTickerAlias>()
                .Where(row => row.EquityListingId == originalId)
                .Select(row => row.Ticker)
                .ToListAsync()
        )
            .Should()
            .Contain("ALTR");
        (await DbContext.Set<EquityDirectorySourceRecord>().CountAsync()).Should().Be(3);
    }

    [Theory]
    [InlineData("two-cusip-owners")]
    [InlineData("different-lei")]
    [InlineData("occupied-venue-symbol")]
    public async Task ConflictingIdentity_LeavesEveryExistingRowAndTheFailedGraphUntouched(
        string scenario
    )
    {
        var first = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "FIRST",
            Cusip: "02209Y100"
        );
        if (scenario == "different-lei")
            first.LegalEntityIdentifier = "5493001KJTIIGC8Y1R12";
        if (scenario == "occupied-venue-symbol")
        {
            first.Presentation.Listing.MarketCountryCode = "PT";
            first.Presentation.Listing.MarketIdentifierCode = "XLIS";
            first.Presentation.Listing.Ticker = "ALTR";
        }
        DbContext.Add(first);
        if (scenario == "two-cusip-owners")
            DbContext.Add(
                Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "SECOND", Cusip: "02209Y100")
            );
        await DbContext.SaveChangesAsync();
        var before = await Snapshot();
        await using var services = Services();
        var importer = new EquityDirectoryIdentityImporter(
            services.GetRequiredService<IServiceScopeFactory>()
        );
        var import = () => importer.ImportListing(Input());
        await import.Should().ThrowAsync<InvalidDataException>();
        await DbContext.SaveChangesAsync();
        (await Snapshot()).Should().Be(before);
        (await DbContext.Set<EquityIssuerSourceIdentifier>().CountAsync()).Should().Be(0);
        (await DbContext.Set<EquityDirectorySourceRecord>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UnknownCurrency_RemainsUnknownUntilTheSameSourceStatesItsUnits()
    {
        await using var services = Services();
        var importer = new EquityDirectoryIdentityImporter(
            services.GetRequiredService<IServiceScopeFactory>()
        );
        var id = await importer.ImportListing(Input(currency: null));
        var pending = await DbContext
            .Set<EquityListing>()
            .AsNoTracking()
            .SingleAsync(row => row.Id == id);
        pending.TradingCurrency.Should().BeNull();
        pending.QuoteUnitMultiplier.Should().BeNull();
        pending.IdentityState.Should().Be(EquityIdentityState.Legacy);
        (await importer.ImportListing(Input())).Should().Be(id);
        var verified = await DbContext
            .Set<EquityListing>()
            .AsNoTracking()
            .SingleAsync(row => row.Id == id);
        verified.TradingCurrency.Should().Be("EUR");
        verified.QuoteUnitMultiplier.Should().Be(1m);
        verified.IdentityState.Should().Be(EquityIdentityState.Verified);
    }

    [Theory]
    [InlineData("isin")]
    [InlineData("related-isin")]
    [InlineData("lei")]
    public async Task InvalidIdentifierCheckDigit_CannotEstablishIssuerOwnership(string field)
    {
        var existing = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "ASGSY",
            Cusip: "02209Y100"
        );
        DbContext.Add(existing);
        await DbContext.SaveChangesAsync();
        var before = await Snapshot();
        var input = Input();
        if (field == "isin")
            input.Isin = "PTALT0AE0003";
        if (field == "related-isin")
            input.RelatedIsins = ["PTALT0AE0002", "US02209Y1000"];
        if (field == "lei")
            input.LegalEntityIdentifier = "213800AKSTYRLHY3X498";
        await using var services = Services();
        var importer = new EquityDirectoryIdentityImporter(
            services.GetRequiredService<IServiceScopeFactory>()
        );
        var import = () => importer.ImportListing(input);
        await import.Should().ThrowAsync<InvalidDataException>();
        (await Snapshot()).Should().Be(before);
        (await DbContext.Set<EquityIssuerSourceIdentifier>().CountAsync()).Should().Be(0);
        (await DbContext.Set<EquityDirectorySourceRecord>().CountAsync()).Should().Be(0);
    }

    private async Task<string> Snapshot() =>
        string.Join(
            "\n",
            await DbContext
                .Database.SqlQueryRaw<string>(
                    """
                    SELECT item::text AS "Value" FROM (
                        SELECT 'issuer' kind, to_jsonb(row) item FROM "EquityIssuer" row
                        UNION ALL SELECT 'security', to_jsonb(row) FROM "EquitySecurity" row
                        UNION ALL SELECT 'listing', to_jsonb(row) FROM "EquityListing" row
                        UNION ALL SELECT 'presentation', to_jsonb(row) FROM "EquityIssuerPresentation" row
                    ) captured ORDER BY kind, item::text
                    """
                )
                .ToListAsync()
        );
}
