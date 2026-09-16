using System.Diagnostics;
using Equibles.Integrations.Bme;
using Equibles.UnitTests.Euronext;

namespace Equibles.UnitTests.EquityMarkets;

public class BmeTests
{
    private static Task<string> Fixture(string name) =>
        File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "EquityMarkets", "Bme", name)
        );

    [Fact]
    public async Task ListedCompanies_ReadsOneMainLinePerCompanyWithoutATicker()
    {
        var list = BmeParser.ReadListedCompanies(await Fixture("listed-companies.sample.json"));
        list.TotalResults.Should().Be(5);
        list.Companies.Should().HaveCount(5);
        var grifols = list.Companies.Single(company => company.Isin == "ES0171996087");
        grifols.Name.Should().Be("GRIFOLS, S.A.");
        grifols.ShareName.Should().Be("GRIFOLS CLASE A");
        grifols.CompanyKey.Should().Be("71996");
        grifols.TradingSystem.Should().Be("SIBE");
        list.Companies.Should().Contain(company => company.Isin == "NL0015001FS8");
    }

    [Theory]
    [InlineData("more", "\"hasMoreResults\": false", "\"hasMoreResults\": true")]
    [InlineData("partial", "\"totalResults\": 5", "\"totalResults\": 123")]
    [InlineData("bad-isin", "\"isin\": \"ES0125220311\"", "\"isin\": \"ES0125220312\"")]
    [InlineData("duplicate", "\"isin\": \"ES0125220311\"", "\"isin\": \"ES0113900J37\"")]
    [InlineData("other-system", "\"tradingSystem\": \"SIBE\"", "\"tradingSystem\": \"MAB\"")]
    [InlineData("no-name", "\"name\": \"ACCIONA,S.A.\"", "\"name\": \"\"")]
    public async Task ChangedShapeOrInvalidCompany_RefusesTheWholeList(
        string scenario,
        string original,
        string replacement
    )
    {
        var json = await Fixture("listed-companies.sample.json");
        json.Should().Contain(original, scenario);
        var read = () => BmeParser.ReadListedCompanies(json.Replace(original, replacement));
        read.Should().Throw<InvalidDataException>(scenario);
        var notJson = () => BmeParser.ReadListedCompanies("[]");
        notJson.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task ShareDetails_StateTheTickerTheIssuerAndTheIssuersOtherLines()
    {
        var acciona = BmeParser.ReadShareDetails(await Fixture("share-details.ES0125220311.json"));
        acciona.Name.Should().Be("ACCIONA");
        acciona.Ticker.Should().Be("ANA");
        acciona.IssuerCode.Should().Be("25220");
        acciona.TradingSystem.Should().Be("SIBE");
        acciona.Market.Should().Be("02");
        acciona.Currency.Should().Be("EUR");
        acciona.Active.Should().BeEmpty();
        acciona.OtherSharesFromIssuer.Should().BeEmpty();
        var grifols = BmeParser.ReadShareDetails(await Fixture("share-details.ES0171996087.json"));
        grifols.Ticker.Should().Be("GRF");
        grifols.OtherSharesFromIssuer.Should().HaveCount(5);
        var current = grifols.OtherSharesFromIssuer.Where(share => share.IsCurrent).ToList();
        current.Should().ContainSingle().Which.Isin.Should().Be("ES0171996095");
        grifols
            .OtherSharesFromIssuer.Where(share => !share.IsCurrent)
            .Should()
            .AllSatisfy(share =>
            {
                share.Active.Should().Be("C");
                share.ExclusionDate.Should().MatchRegex("^[0-9]{8}$");
            });
        var classB = BmeParser.ReadShareDetails(await Fixture("share-details.ES0171996095.json"));
        classB.Ticker.Should().Be("GRF.P", "the parser keeps the venue's spelling");
        classB.IssuerCode.Should().Be(grifols.IssuerCode);
        var withoutTicker = (await Fixture("share-details.ES0125220311.json")).Replace(
            "\"ticker\":\"ANA\"",
            "\"ticker\":\"\""
        );
        var noTicker = () => BmeParser.ReadShareDetails(withoutTicker);
        noTicker.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task Client_ReadsTheListAndEachLineFromTheExchangeOriginOnly()
    {
        var handler = new EuronextDirectoryTestHandler([
            await Fixture("listed-companies.sample.json"),
            await Fixture("share-details.ES0125220311.json"),
        ]);
        using var http = new HttpClient(handler);
        var pace = new CountingRateLimiter();
        var client = new BmeClient(http) { Pace = pace };
        var list = await client.GetListedCompanies();
        list.SourceUrl.Should().Be(BmeClient.ListedCompaniesUrl);
        list.Companies.Should().HaveCount(5);
        var details = await client.GetShareDetails("ES0125220311");
        details
            .SourceUrl.AbsoluteUri.Should()
            .Be(
                "https://apiweb.bolsasymercados.es/Market/v1/EQ/ShareDetailsInfo?ISIN=ES0125220311"
            );
        handler
            .Requests.Should()
            .AllSatisfy(request =>
            {
                request.Method.Should().Be(HttpMethod.Get);
                request.Url.Host.Should().Be("apiweb.bolsasymercados.es");
            });
        pace.Waits.Should().Be(2);
        using var other = new HttpClient(
            new EuronextDirectoryTestHandler([await Fixture("share-details.ES0125220311.json")])
        );
        var mismatch = () =>
            new BmeClient(other) { Pace = new CountingRateLimiter() }.GetShareDetails(
                "ES0113900J37"
            );
        await mismatch
            .Should()
            .ThrowAsync<InvalidDataException>()
            .WithMessage("*another security*");
    }

    [Fact]
    public async Task Client_LeavesASecondBetweenRequestsWhicheverInstanceMakesThem()
    {
        BmeClient.MinimumRequestIntervalSeconds.Should().Be(1);
        using var first = new HttpClient(
            new EuronextDirectoryTestHandler([await Fixture("share-details.ES0125220311.json")])
        );
        using var second = new HttpClient(
            new EuronextDirectoryTestHandler([await Fixture("share-details.ES0113900J37.json")])
        );
        var started = Stopwatch.GetTimestamp();
        await new BmeClient(first).GetShareDetails("ES0125220311");
        await new BmeClient(second).GetShareDetails("ES0113900J37");
        Stopwatch.GetElapsedTime(started).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
    }
}
