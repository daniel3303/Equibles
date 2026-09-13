using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(HistoricalEquityDbCollection.Name)]
public class NativeEquityProfileTests : ParadeDbMcpTestBase
{
    public NativeEquityProfileTests(HistoricalEquityDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task MixedWriters_OnlyChangedLegacyFieldsAndMembershipsReachNativeProfiles()
    {
        var source = new CommonStock
        {
            Ticker = "ORIGINAL",
            Name = "Original name",
            Cik = "0000000123",
            SharesOutStanding = 100,
            MarketCapitalization = 1000,
            SecondaryTickers = ["SECONDARY"],
            ReferenceTickers = ["REFERENCE"],
        };
        DbContext.Add(source);
        await DbContext.SaveChangesAsync();
        var issuer = await new EquityIssuerRepository(DbContext).GetByCik(source.Cik);
        var primary = issuer.Presentation.Listing;
        var secondary = issuer
            .Securities.SelectMany(row => row.Listings)
            .Single(row => row.Ticker == "SECONDARY");
        var reference = issuer
            .Securities.SelectMany(row => row.Listings)
            .Single(row => row.Ticker == "REFERENCE");
        var checkpoint = new DateTime(2026, 9, 12, 1, 2, 3, DateTimeKind.Utc);
        issuer.Name = "Native name";
        issuer.Description = "Native description";
        primary.Security.SharesOutstanding = 200;
        primary.Security.MarketCapitalization = 3000;
        primary.YahooEnrichmentAttemptedAt = checkpoint;
        primary.PriceHistoryBackfilled = true;
        secondary.IsDirectoryListed = false;
        secondary.Active = false;
        reference.IsReferenceListed = false;
        issuer.Presentation.Listing = secondary;
        await DbContext.SaveChangesAsync();

        source.Website = "https://updated.example.com";
        await DbContext.SaveChangesAsync();
        // A different legacy membership changes next; unchanged members remain native-owned.
        source.SecondaryTickers = ["SECONDARY", "NEW-SECONDARY"];
        await DbContext.SaveChangesAsync();
        await using var read = Fixture.CreateDbContext();
        var saved = await new EquityIssuerRepository(read).GetByCik(source.Cik);
        saved.Name.Should().Be("Native name");
        saved.Description.Should().Be("Native description");
        saved.Website.Should().Be(source.Website);
        saved.Presentation.EquityListingId.Should().Be(secondary.Id);
        var listings = saved.Securities.SelectMany(row => row.Listings).ToList();
        var savedPrimary = listings.Single(row => row.Id == primary.Id);
        savedPrimary.Security.SharesOutstanding.Should().Be(200);
        savedPrimary.Security.MarketCapitalization.Should().Be(3000);
        savedPrimary.YahooEnrichmentAttemptedAt.Should().Be(checkpoint);
        savedPrimary.PriceHistoryBackfilled.Should().BeTrue();
        listings.Single(row => row.Id == secondary.Id).Active.Should().BeFalse();
        listings.Single(row => row.Id == secondary.Id).IsDirectoryListed.Should().BeFalse();
        listings.Single(row => row.Id == reference.Id).IsReferenceListed.Should().BeFalse();
        listings.Single(row => row.Ticker == "NEW-SECONDARY").IsDirectoryListed.Should().BeTrue();

        source.SharesOutStanding = 250;
        await DbContext.SaveChangesAsync();
        await read.Entry(savedPrimary.Security).ReloadAsync();
        savedPrimary.Security.SharesOutstanding.Should().Be(250);
        savedPrimary.Security.MarketCapitalization.Should().Be(3000);
    }

    [Fact]
    public async Task LegacyReactivation_ClearsTheListingCutoffInTheSameWrite()
    {
        var source = new CommonStock
        {
            Ticker = "RETURNED",
            Active = false,
            DelistedOn = new DateOnly(2025, 9, 10),
        };
        DbContext.Add(source);
        await DbContext.SaveChangesAsync();
        var listing = await Equibles
            .TestSupport.LegacyEquityTestMappings.GetListing(DbContext, source.Id, source.Ticker)
            .SingleAsync();
        listing.Active.Should().BeFalse();
        listing.DelistedOn.Should().Be(source.DelistedOn);
        source.Active = true;
        source.DelistedOn = null;
        await DbContext.SaveChangesAsync();
        await DbContext.Entry(listing).ReloadAsync();
        listing.Active.Should().BeTrue();
        listing.DelistedOn.Should().BeNull();
    }

    [Fact]
    public async Task LegacyProfileChange_CannotOverwriteAVerifiedListingLifecycle()
    {
        var source = new CommonStock { Ticker = "VERIFIED" };
        DbContext.Add(source);
        await DbContext.SaveChangesAsync();
        var listing = await Equibles
            .TestSupport.LegacyEquityTestMappings.GetListing(DbContext, source.Id, source.Ticker)
            .SingleAsync();
        listing.MarketIdentifierCode = "XNYS";
        listing.TradingCurrency = "USD";
        listing.QuoteUnitMultiplier = 1m;
        listing.IdentitySourceUrl = "https://example.com/official-listing";
        listing.IdentityState = EquityIdentityState.Verified;
        await DbContext.SaveChangesAsync();
        source.Active = false;
        source.DelistedOn = new DateOnly(2026, 9, 10);
        await DbContext.SaveChangesAsync();
        await DbContext.Entry(listing).ReloadAsync();
        listing.Active.Should().BeTrue();
        listing.DelistedOn.Should().BeNull();
    }

    [Fact]
    public async Task Presentation_RejectsAnotherIssuersListing_AndAnInvalidatingReassignment()
    {
        var owner = new EquityIssuer { Name = "Owner" };
        var other = new EquityIssuer { Name = "Other" };
        var security = new EquitySecurity { Issuer = owner };
        var listing = new EquityListing { Security = security, Ticker = "OWNED" };
        DbContext.AddRange(listing, other);
        await DbContext.SaveChangesAsync();
        var wrong = new EquityIssuerPresentation { Issuer = other, Listing = listing };
        DbContext.Add(wrong);
        Func<Task> saveWrong = () => DbContext.SaveChangesAsync();
        await saveWrong.Should().ThrowAsync<DbUpdateException>();
        DbContext.Entry(wrong).State = EntityState.Detached;
        DbContext.Add(new EquityIssuerPresentation { Issuer = owner, Listing = listing });
        await DbContext.SaveChangesAsync();
        security.Issuer = other;
        Func<Task> moveSecurity = () => DbContext.SaveChangesAsync();
        await moveSecurity.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task LegacyProfile_MovesFactsToTheirNativeOwners_WithoutCopyingPrimaryEconomicsToSiblings()
    {
        var source = new CommonStock
        {
            Ticker = "PRIMARY",
            Name = "Issuer",
            Description = "Original description",
            Cik = "0000123456",
            SecondaryCiks = ["0000654321"],
            Website = "https://example.com",
            FiscalYearEndMonth = 9,
            FiscalYearEndDay = 30,
            Sic = "1234",
            EntityType = "operating",
            SharesOutStanding = 1234567,
            MarketCapitalization = 987654321.125,
            Cusip = "123456789",
            ListedSecurityType = ListedSecurityType.CommonShares,
            ListedSecurityTitle = "Class A ordinary shares",
            SecondaryTickers = ["SECONDARY", "FUND"],
            ReferenceTickers = ["FUND"],
            PriceHistoryBackfilledTickers = ["SECONDARY", "HISTORICAL-ONLY"],
        };
        DbContext.Add(source);
        await DbContext.SaveChangesAsync();
        var issuer = await new EquityIssuerRepository(DbContext).GetByCik(source.Cik);
        issuer.Description.Should().Be(source.Description);
        issuer.SecondaryCiks.Should().Equal(source.SecondaryCiks);
        issuer.Website.Should().Be(source.Website);
        issuer.FiscalYearEndMonth.Should().Be(9);
        issuer.FiscalYearEndDay.Should().Be(30);
        issuer.Sic.Should().Be(source.Sic);
        issuer.EntityType.Should().Be(source.EntityType);
        var primary = issuer.Presentation.Listing;
        primary.Ticker.Should().Be("PRIMARY");
        primary.Security.Cusip.Should().Be(source.Cusip);
        primary.Security.SharesOutstanding.Should().Be(source.SharesOutStanding);
        primary.Security.MarketCapitalization.Should().Be(source.MarketCapitalization);
        primary.Security.RegistrationTitle.Should().Be(source.ListedSecurityTitle);
        primary.Security.RegistrationType.Should().Be(source.ListedSecurityType);
        primary.Security.SecurityType.Should().Be(EquitySecurityKind.Unknown);
        var secondary = await Equibles
            .TestSupport.LegacyEquityTestMappings.GetListing(DbContext, source.Id, "SECONDARY")
            .SingleAsync();
        secondary.PriceHistoryBackfilled.Should().BeTrue();
        var historical = await Equibles
            .TestSupport.LegacyEquityTestMappings.GetListing(
                DbContext,
                source.Id,
                "HISTORICAL-ONLY"
            )
            .SingleAsync();
        historical.PriceHistoryBackfilled.Should().BeTrue();
        historical.Active.Should().BeFalse();
        secondary.Security.SharesOutstanding.Should().Be(0);
        secondary.Security.Cusip.Should().BeNull();
        var fund = await Equibles
            .TestSupport.LegacyEquityTestMappings.GetListing(DbContext, source.Id, "FUND")
            .SingleAsync();
        fund.IsReferenceListed.Should().BeTrue();
        primary.IsReferenceListed.Should().BeFalse();

        source.Website = "https://updated.example.com";
        await DbContext.SaveChangesAsync();
        await DbContext.Entry(issuer).ReloadAsync();
        issuer.Website.Should().Be(source.Website);
    }

    [Fact]
    public async Task NativeIssuerGraph_CanPersistWithoutAnyLegacyStock()
    {
        var issuer = new EquityIssuer { Name = "Native issuer", Website = "https://example.com" };
        var security = new EquitySecurity { Issuer = issuer };
        var listing = new EquityListing { Security = security, Ticker = "NATIVE" };
        issuer.Presentation = new EquityIssuerPresentation { Issuer = issuer, Listing = listing };
        DbContext.Add(issuer);
        await DbContext.SaveChangesAsync();
        (await DbContext.Set<CommonStock>().CountAsync()).Should().Be(0);
        (await DbContext.Set<LegacyEquityListing>().CountAsync()).Should().Be(0);
        DbContext.ChangeTracker.Clear();
        var saved = await DbContext.Set<EquityIssuer>().SingleAsync();
        saved.Presentation.Listing.Id.Should().Be(listing.Id);
        saved.Presentation.Listing.Security.EquityIssuerId.Should().Be(saved.Id);
    }
}
