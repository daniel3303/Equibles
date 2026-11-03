using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.CommonStocks;

// A company quoted over the counter in the US shows its verified home-market listing; one a US exchange lists keeps its
// US page. SEC's ticker directory decides, over a window anchored on its own newest capture.
[Collection(ParadeDbCollection.Name)]
public class EquityIssuerVenuePresentationTests : ParadeDbMcpTestBase
{
    private static readonly DateOnly Newest = new(2026, 10, 30);

    public EquityIssuerVenuePresentationTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task AnOverTheCounterLineYieldsToTheVerifiedHomeListing()
    {
        var issuer = Canadian("DRYGF", "1001", "DRYG");
        await Seed(issuer);
        Observe("1001", "DRYGF", "OTC", Newest, Newest);
        await DbContext.SaveChangesAsync();

        var presented = await Presentations();

        var venue = presented.Should().ContainKey(issuer.Id).WhoseValue;
        (venue.MarketIdentifierCode, venue.Ticker, venue.UsTicker)
            .Should()
            .Be(("XTSX", "DRYG", "DRYGF"));
    }

    [Fact]
    public async Task ALineNoLongerInTheDirectoryYieldsToo()
    {
        var issuer = Canadian("GPUSF", "1002", "GPUS");
        await Seed(issuer);
        Observe("9999", "OTHER", "NYSE", Newest, Newest);
        await DbContext.SaveChangesAsync();

        (await Presentations()).Should().ContainKey(issuer.Id);
    }

    [Fact]
    public async Task AnExchangeListedLineKeepsItsUsPageThroughADroppedCapture()
    {
        var listed = Canadian("SHOP", "1003", "SHOP");
        var gap = Canadian("TECK", "1004", "TECK.B");
        await Seed(listed, gap);
        Observe("1003", "SHOP", "NYSE", Newest.AddDays(-12), Newest);
        Observe("1004", "TECK", "NYSE", Newest.AddDays(-12), Newest.AddDays(-2));
        await DbContext.SaveChangesAsync();

        (await Presentations()).Should().BeEmpty();
    }

    [Fact]
    public async Task ALineMovedToOverTheCounterYieldsOnceItsExchangeRowLeavesTheWindow()
    {
        var moved = Canadian("MDALF", "1005", "MDA");
        var recent = Canadian("RCNTF", "1006", "RCNT");
        await Seed(moved, recent);
        Observe("1005", "MDALF", "Nasdaq", Newest.AddDays(-30), Newest.AddDays(-8));
        Observe("1005", "MDALF", "OTC", Newest.AddDays(-7), Newest);
        Observe("1006", "RCNTF", "Nasdaq", Newest.AddDays(-30), Newest.AddDays(-7));
        Observe("1006", "RCNTF", "OTC", Newest.AddDays(-6), Newest);
        await DbContext.SaveChangesAsync();

        var presented = await Presentations();

        presented.Keys.Should().BeEquivalentTo([moved.Id]);
    }

    // SEC can list a filer's OTC symbol first and its NYSE symbol second; any exchange row for the filer keeps its US page,
    // as does an exchange row for its US ticker under another CIK.
    [Fact]
    public async Task AnExchangeRowForAnotherTickerOrCikKeepsTheUsPage()
    {
        var sibling = Canadian("SIBLF", "1009", "SIBL");
        var otherCik = Canadian("OCIKF", "1010", "OCIK");
        await Seed(sibling, otherCik);
        Observe("1009", "SIBLF", "OTC", Newest, Newest);
        Observe("1009", "SIBL", "NYSE", Newest, Newest);
        Observe("2010", "OCIKF", "Nasdaq", Newest, Newest);
        await DbContext.SaveChangesAsync();

        (await Presentations()).Should().BeEmpty();
    }

    // The ledger stores SEC's tickers in their identity form, so a class suffix still matches.
    [Fact]
    public async Task AClassTickerMatchesTheDirectorysIdentityForm()
    {
        var issuer = Canadian("BBD-B", "1007", "BBD.B");
        await Seed(issuer);
        Observe("1007", "BBDB", "NYSE", Newest, Newest);
        await DbContext.SaveChangesAsync();

        (await Presentations()).Should().BeEmpty();
    }

    [Fact]
    public async Task AnEmptyDirectoryProvesNothing()
    {
        await Seed(Canadian("DRYGF", "1001", "DRYG"));

        (await Presentations()).Should().BeEmpty();
    }

    // A US-incorporated company with a Canadian line and no ISIN has no home-market proof, so its US page stays.
    [Fact]
    public async Task AVenueListingWithoutHomeProofNeverPresents()
    {
        var issuer = Canadian("USOTF", "1008", "USOT");
        issuer.StateOfIncorporation = "NV";
        await Seed(issuer);
        Observe("1008", "USOTF", "OTC", Newest, Newest);
        await DbContext.SaveChangesAsync();

        (await Presentations()).Should().BeEmpty();
    }

    private Task<
        IReadOnlyDictionary<Guid, Equibles.CommonStocks.Repositories.Models.EquityVenuePresentation>
    > Presentations() => new EquityIssuerRepository(DbContext).GetVenuePresentations();

    private static EquityIssuer Canadian(string usTicker, string cik, string venueTicker)
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: usTicker,
            Name: $"{venueTicker} Corp",
            Cik: cik
        );
        issuer.StateOfIncorporation = "A1";
        var security = issuer.Presentation.Listing.Security;
        security.Listings.Add(
            new EquityListing
            {
                Security = security,
                EquitySecurityId = security.Id,
                Ticker = venueTicker,
                MarketCountryCode = "CA",
                MarketIdentifierCode = "XTSX",
                IdentityState = EquityIdentityState.Verified,
                IdentitySourceUrl = "https://example.test/tmx",
                TradingCurrency = "CAD",
                QuoteUnitMultiplier = 1m,
                Active = true,
                IsDirectoryListed = true,
            }
        );
        return issuer;
    }

    private async Task Seed(params EquityIssuer[] issuers)
    {
        DbContext.Set<EquityIssuer>().AddRange(issuers);
        await DbContext.SaveChangesAsync();
    }

    private void Observe(
        string cik,
        string ticker,
        string exchange,
        DateOnly first,
        DateOnly last
    ) =>
        DbContext
            .Set<SecTickerDirectoryObservation>()
            .Add(
                new SecTickerDirectoryObservation
                {
                    Cik = cik,
                    Ticker = ticker,
                    Exchange = exchange,
                    FirstObservedOn = first,
                    LastObservedOn = last,
                }
            );
}
