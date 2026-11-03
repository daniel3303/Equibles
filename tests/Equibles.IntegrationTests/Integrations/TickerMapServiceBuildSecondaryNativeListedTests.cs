using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Worker;

namespace Equibles.IntegrationTests.Integrations;

// FINRA reports every listed line under its own symbol, so a filer's second share class
// (GOOG beside GOOGL) needs its own claim; these pins keep that claim from ever taking a symbol
// another line holds.
public class TickerMapServiceBuildSecondaryNativeListedTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly EquityIssuerRepository _issuerRepository;
    private readonly TickerMapService _service;

    public TickerMapServiceBuildSecondaryNativeListedTests()
    {
        _dbContext = TestDbContextFactory.Create(new CommonStocksModuleConfiguration());
        _issuerRepository = new EquityIssuerRepository(_dbContext);
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(EquityIssuerRepository), _issuerRepository),
            (typeof(EquityListingRepository), new EquityListingRepository(_dbContext))
        );
        _service = new TickerMapService(scopeFactory);
    }

    public void Dispose() => _dbContext.Dispose();

    private static EquityIssuer CreateIssuer(
        string ticker,
        string name,
        List<string> secondaryTickers = null
    ) =>
        Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: ticker,
            Name: name,
            Cik: $"CIK-{ticker}",
            SecondaryTickers: secondaryTickers
        );

    private async Task Seed(params EquityIssuer[] issuers)
    {
        _issuerRepository.AddRange(issuers);
        await _issuerRepository.SaveChanges();
    }

    private async Task<Dictionary<string, EquityListingReference>> BuildAfterPrimaryClaims()
    {
        var claimed = await _service.BuildListed(null, CancellationToken.None);
        return await _service.BuildSecondaryNativeListed(
            claimed.Keys,
            null,
            CancellationToken.None
        );
    }

    private static EquityListing Line(EquityIssuer issuer, string ticker) =>
        issuer.Securities.SelectMany(security => security.Listings).Single(l => l.Ticker == ticker);

    [Fact]
    public async Task ClaimsTheFilersOtherDirectoryListedLineAtItsExactListing()
    {
        var alphabet = CreateIssuer("GOOGL", "Alphabet Inc.", ["GOOG"]);
        await Seed(alphabet);

        var result = await BuildAfterPrimaryClaims();

        result
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new KeyValuePair<string, EquityListingReference>(
                    "GOOG",
                    new(Line(alphabet, "GOOG").Id, alphabet.Id, "GOOG")
                )
            );
    }

    [Fact]
    public async Task LeavesReferenceAndUnlistedLinesToTheirOwnRules()
    {
        var trust = CreateIssuer("VB", "Vanguard Index Funds");
        Equibles.TestSupport.EquityIssuerSeed.SetReferenceTickers(trust, ["VOO"]);
        // Also directory-listed, so only the reference rule keeps it out of this map.
        Line(trust, "VOO").IsDirectoryListed = true;
        // A stored line the exchange directory no longer lists carries no claim.
        UsEquityDirectory.GetOrAddListing(trust, "VBOLD");
        await Seed(trust);

        var result = await _service.BuildSecondaryNativeListed([], null, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task NeverTakesATickerAnotherIssuersLineHolds()
    {
        var holder = CreateIssuer("CLASH", "Primary Holder Corp");
        var stale = CreateIssuer("OLDCO", "Stale Line Corp", ["CLASH"]);
        var first = CreateIssuer("AAA", "First Dual Corp", ["DUAL"]);
        var second = CreateIssuer("BBB", "Second Dual Corp", ["DUAL"]);
        await Seed(holder, stale, first, second);

        var claimed = await _service.BuildListed(null, CancellationToken.None);
        var result = await _service.BuildSecondaryNativeListed(
            claimed.Keys,
            null,
            CancellationToken.None
        );

        result.Should().BeEmpty();
        claimed["CLASH"].Should().Be(new ListedSecurityKey(holder.Id, "CLASH"));
    }

    [Fact]
    public async Task NeverTakesATickerTheCallerAlreadyClaimed()
    {
        // The primary map claims reference tickers whatever their trading state, so an inactive
        // reference line still owns its symbol against another filer's active line.
        var trust = CreateIssuer("TRST", "Reference Trust");
        Equibles.TestSupport.EquityIssuerSeed.SetReferenceTickers(trust, ["XREF"]);
        Line(trust, "XREF").Active = false;
        var other = CreateIssuer("OTHR", "Other Corp", ["XREF"]);
        await Seed(trust, other);

        var claimed = await _service.BuildListed(null, CancellationToken.None);
        var result = await _service.BuildSecondaryNativeListed(
            claimed.Keys,
            null,
            CancellationToken.None
        );

        claimed["XREF"].Should().Be(new ListedSecurityKey(trust.Id, "XREF"));
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DropsARetiredLineAndTheDottedSpellingOfThePresentationTicker()
    {
        var berkshire = CreateIssuer("BRK-B", "Berkshire Hathaway Inc.", ["BRK.B", "BRK-A"]);
        await Seed(berkshire);
        _issuerRepository.AddDelistedListing(
            new EquityListingRetirementEvidence
            {
                EquityIssuerId = berkshire.Id,
                ListedTicker = "BRK.A",
                DelistedOn = new DateOnly(2026, 1, 1),
            }
        );
        await _issuerRepository.SaveChanges();

        var result = await BuildAfterPrimaryClaims();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FilterKeepsOnlyRequestedLines()
    {
        var fox = CreateIssuer("FOXA", "Fox Corporation", ["FOX"]);
        var news = CreateIssuer("NWSA", "News Corporation", ["NWS"]);
        await Seed(fox, news);

        var result = await _service.BuildSecondaryNativeListed([], ["NWS"], CancellationToken.None);

        result.Keys.Should().Equal("NWS");
        result["NWS"].EquityIssuerId.Should().Be(news.Id);
    }
}
