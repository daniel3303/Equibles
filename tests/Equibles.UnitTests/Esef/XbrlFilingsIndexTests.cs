using Equibles.Integrations.Common.Http;
using Equibles.Integrations.XbrlFilings;
using Equibles.Integrations.XbrlFilings.Models;
using FluentAssertions;

namespace Equibles.UnitTests.Esef;

// Read against byte-verbatim captures of the host's own index; see TestAssets/Esef/README.md.
public class XbrlFilingsIndexTests
{
    private static readonly Uri Origin = new("https://filings.xbrl.org");

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "Esef", name));

    private static XbrlFilingPage FrenchPage() =>
        XbrlFilingsParser.Read(Fixture("filings-fr-page.json"), Origin);

    [Fact]
    public void Parser_ReadsTheFilerIdentityFromTheIncludedEntity()
    {
        var page = FrenchPage();

        page.TotalCount.Should()
            .Be(1179, "the host states the whole country's count on every page");
        page.Filings.Should().HaveCount(3);
        page.Filings[0].EntityIdentifier.Should().Be("549300HGBVWX4FC44K67");
        page.Filings[0].EntityName.Should().Be("ATLAND");
    }

    [Fact]
    public void Parser_TakesEveryAddressVerbatim()
    {
        var filing = FrenchPage().Filings[0];

        filing
            .PackageUrl.AbsolutePath.Should()
            .Be(
                "/549300HGBVWX4FC44K67/2022-12-31/ESEF/FR/0/549300HGBVWX4FC44K67-2022-12-31-fr.zip"
            );
        filing.ReportUrl.Host.Should().Be("filings.xbrl.org");
        filing.Sha256.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Parser_ReadsTheRegimeFromTheEndOfTheKey()
    {
        FrenchPage().Filings.Should().OnlyContain(filing => filing.Regime == "ESEF");
    }

    // A Ukrainian identifier carries a hyphen of its own, which is why the regime is read from the END of
    // the key: counting segments from the front would land on the registry number.
    [Fact]
    public void Parser_ReadsTheRegimeWhenTheIdentifierItselfCarriesAHyphen()
    {
        var page = XbrlFilingsParser.Read(Fixture("filings-ua-page.json"), Origin);

        page.Filings.Should().NotBeEmpty();
        page.Filings.Should().OnlyContain(filing => filing.Regime == "UAIFRS");
    }

    [Fact]
    public void Selection_RefusesARowThatIsNeitherEsefNorCarriesALegalEntityIdentifier()
    {
        var page = XbrlFilingsParser.Read(Fixture("filings-ua-page.json"), Origin);

        page.Filings.Should()
            .NotContain(
                filing => EsefFilingSelection.IsEsefWithLegalEntityIdentifier(filing),
                "the regime is not ESEF"
            );
        page.Filings.Should()
            .OnlyContain(
                filing =>
                    !Equibles.Core.Identity.InternationalSecurityIdentifiers.IsValidLei(
                        filing.EntityIdentifier
                    ),
                "an EDRPOU registry number is not an LEI either"
            );
    }

    [Fact]
    public void Selection_AdmitsARealEsefRow()
    {
        FrenchPage()
            .Filings.Should()
            .OnlyContain(filing => EsefFilingSelection.IsEsefWithLegalEntityIdentifier(filing));
    }

    [Fact]
    public void IndexUrl_StaysOnTheHostAndCarriesTheFilter()
    {
        var url = XbrlFilingsClient.IndexUrl("FR", 2, 100);

        url.Host.Should().Be("filings.xbrl.org");
        url.Query.Should().Contain("filter%5Bcountry%5D=FR").And.Contain("page%5Bnumber%5D=2");
        url.Query.Should()
            .Contain("include=entity", "the filer's identity comes from the entity resource");
    }

    [Fact]
    public void IndexUrl_ForTheWholeCorpus_CarriesNoCountryFilter()
    {
        var url = XbrlFilingsClient.IndexUrl(3, 100);

        url.Host.Should().Be("filings.xbrl.org");
        // A filing's country is where the report was FILED, which need not be the country of the market the
        // issuer is listed on, so the capture reads the corpus rather than a list of market countries.
        url.Query.Should().NotContain("filter");
        url.Query.Should().Contain("page%5Bnumber%5D=3").And.Contain("page%5Bsize%5D=100");
        url.Query.Should().Contain("include=entity");
    }

    // The caller's own ceiling reaches the fetch, so a report it could not use is abandoned rather than
    // downloaded whole: the tail of this corpus runs to 125 MB.
    [Fact]
    public async Task GetReport_RefusesABodyPastTheCallersCeiling()
    {
        const string path = "/report.xhtml";
        var client = new XbrlFilingsClient(
            new HttpClient(
                new EsefIndexTestHandler(
                    new Dictionary<string, string> { [path] = new('a', 4_096) }
                )
            )
        )
        {
            Pace = new Equibles.Integrations.Common.RateLimiter.RateLimiter(
                1000,
                TimeSpan.FromSeconds(1)
            ),
        };

        var refuse = () => client.GetReport(new Uri(Origin, path), 64);
        await refuse.Should().ThrowAsync<SameOriginSizeException>();

        var read = await client.GetReport(new Uri(Origin, path), 8_192);
        read.Bytes.Should().HaveCount(4_096);
    }

    // A host that states its length is refused before its body is read. This one does not state one, so
    // the check below is the cheap half of the contract rather than the one production takes.
    [Fact]
    public async Task GetReport_RefusesOnTheStatedLengthBeforeReadingTheBody()
    {
        const string path = "/report.xhtml";
        var handler = new EsefIndexTestHandler(
            new Dictionary<string, string> { [path] = new('a', 16) }
        )
        {
            OverstatedContentLength = 200_000_000,
        };
        var client = new XbrlFilingsClient(new HttpClient(handler))
        {
            Pace = new Equibles.Integrations.Common.RateLimiter.RateLimiter(
                1000,
                TimeSpan.FromSeconds(1)
            ),
        };

        var refuse = () => client.GetReport(new Uri(Origin, path), 1_024);

        await refuse.Should().ThrowAsync<SameOriginSizeException>();
    }

    // What production actually gets: the host answers chunked and states no length, so the ceiling can only
    // be reached by reading the body, and the refusal costs that much transfer rather than nothing. It is
    // bounded there and does not read the whole report, which is why both halves are asserted.
    [Fact]
    public async Task GetReport_WhenTheHostStatesNoLength_PaysUpToTheCeilingBeforeRefusing()
    {
        const string path = "/report.xhtml";
        const int bodyLength = 200_000;
        var handler = new EsefIndexTestHandler(
            new Dictionary<string, string> { [path] = new('a', bodyLength) }
        )
        {
            OmitContentLength = true,
        };
        var client = new XbrlFilingsClient(new HttpClient(handler))
        {
            Pace = new Equibles.Integrations.Common.RateLimiter.RateLimiter(
                1000,
                TimeSpan.FromSeconds(1)
            ),
        };

        var refuse = () => client.GetReport(new Uri(Origin, path), 64);

        await refuse.Should().ThrowAsync<SameOriginSizeException>();
        handler.BodyBytesRead.Should().BeGreaterThan(64);
        handler.BodyBytesRead.Should().BeLessThan(bodyLength);
    }
}
