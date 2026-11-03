using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.GovernmentContracts.Data.Models;
using Equibles.GovernmentContracts.Mcp.Tools;
using Equibles.GovernmentContracts.Repositories;
using Equibles.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

// One issuer can hold US awards in US dollars and Canadian contracts in Canadian dollars; every answer reads one
// country, chosen by the symbol's listing unless the caller names one, so the two currencies never share a total.
[Collection(ParadeDbCollection.Name)]
public class GovernmentContractsToolsCountryTests : ParadeDbMcpTestBase
{
    private static readonly DateOnly AwardDay = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30);

    public GovernmentContractsToolsCountryTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private GovernmentContractsTools Sut() =>
        new(
            new GovernmentContractRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            ErrorManager,
            NullLogger<GovernmentContractsTools>()
        );

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        var wajax = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "WJX",
            Name: "Wajax Corporation",
            MarketCountryCode: "CA",
            MarketIdentifierCode: "XTSE",
            IdentityState: EquityIdentityState.Verified,
            TradingCurrency: "CAD",
            QuoteUnitMultiplier: 1m
        );
        wajax.Presentation.Listing.IdentitySourceUrl =
            "https://www.tsx.com/listings/listing-with-us/listed-company-directory";
        DbContext.Add(wajax);
        DbContext.AddRange(
            Contract(wajax, "CA:dnd-mdn:W8486-1", "CA", 250_000m, "National Defence"),
            Contract(
                wajax,
                "CA:pwgsc-tpsgc:EZ1",
                "CA",
                100_000m,
                "Public Services and Procurement Canada"
            ),
            Contract(wajax, "US-AWARD-1", "US", 5_000_000m, "Department of Defense")
        );
        await DbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task ACanadianSymbolReadsCanadianContractsInCanadianDollars()
    {
        var result = await Sut().GetGovernmentContracts("XTSE:WJX");

        result
            .Should()
            .Contain("Government of Canada contracts for XTSE:WJX")
            .And.Contain("2 awards totaling C$350,000")
            .And.Contain("National Defence")
            .And.NotContain("Department of Defense");
    }

    [Fact]
    public async Task AnExplicitCountryReadsThatGovernmentsAwards()
    {
        var us = await Sut().GetGovernmentContracts("XTSE:WJX", country: "US");

        us.Should().Contain("1 awards totaling $5,000,000").And.NotContain("C$");
        (await Sut().GetGovernmentContracts("XTSE:WJX", country: "MX")).Should().Contain("country");
    }

    [Fact]
    public async Task TheRankingSumsOneCountryAtATime()
    {
        var canada = await Sut().GetTopGovernmentContractors(country: "CA");
        var us = await Sut().GetTopGovernmentContractors();

        // A Toronto-only company is named by MIC:TICKER, which the per-company tool reads back.
        canada
            .Should()
            .Contain("Top Government of Canada contractors")
            .And.Contain("| XTSE:WJX |")
            .And.Contain("C$350,000");
        us.Should()
            .Contain("Top federal contractors")
            .And.Contain("$5,000,000")
            .And.NotContain("C$");
    }

    private static GovernmentContract Contract(
        EquityIssuer issuer,
        string key,
        string country,
        decimal amount,
        string agency
    ) =>
        new()
        {
            EquityIssuerId = issuer.Id,
            AwardUniqueKey = key,
            CountryCode = country,
            RecipientName = "WAJAX LIMITED",
            AwardingAgency = agency,
            Amount = amount,
            ActionDate = AwardDay,
            AwardType =
                country == "CA"
                    ? GovernmentContractAwardType.Contract
                    : GovernmentContractAwardType.DefinitiveContract,
        };
}
