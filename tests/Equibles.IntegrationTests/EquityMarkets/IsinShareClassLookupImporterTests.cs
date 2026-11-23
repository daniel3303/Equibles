using System.Net;
using System.Text;
using Equibles.CommonStocks.BusinessLogic.Directory;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.EquityMarkets.BusinessLogic.Directory;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.OpenFigi;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.IntegrationTests.EquityMarkets;

[Collection(ParadeDbCollection.Name)]
public class IsinShareClassLookupImporterTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    private const string Isin = "NL0000235190";

    // OpenFIGI's own answer for Airbus's two jobs: the US composite line, then the over-the-counter quote.
    private const string Answer = """
        [{"data":[{"figi":"BBG000BJJR23","name":"AIRBUS SE","ticker":"EADSF","exchCode":"US","compositeFIGI":"BBG000BJJR23","securityType":"Common Stock","marketSector":"Equity","shareClassFIGI":"BBG001S8TFZ6","securityType2":"Common Stock","securityDescription":"EADSF"}]},
         {"data":[{"figi":"BBG000BJJYG3","name":"AIRBUS SE","ticker":"EADSF","exchCode":"UV","compositeFIGI":"BBG000BJJR23","securityType":"Common Stock","marketSector":"Equity","shareClassFIGI":"BBG001S8TFZ6","securityType2":"Common Stock","securityDescription":"EADSF"}]}]
        """;

    private ServiceProvider Services() =>
        new ServiceCollection()
            .AddScoped<EquiblesFinancialDbContext>(_ => Fixture.CreateDbContext())
            .AddScoped<EquityIssuerRepository>()
            .AddScoped<EquityListingRepository>()
            .AddScoped<EquityDirectorySourceRecordRepository>()
            .AddScoped<IsinShareClassLookupRepository>()
            .BuildServiceProvider();

    private IsinShareClassLookupImporter Importer(ServiceProvider services, Reply reply) =>
        new(
            services.GetRequiredService<IServiceScopeFactory>(),
            new OpenFigiClient(new HttpClient(reply), NullLogger<OpenFigiClient>())
            {
                Pace = new RateLimiter(100, TimeSpan.FromMilliseconds(1)),
            },
            NullLogger<IsinShareClassLookupImporter>()
        );

    private static Task<Guid> SeedListing(ServiceProvider services) =>
        new EquityDirectoryIdentityImporter(
            services.GetRequiredService<IServiceScopeFactory>()
        ).ImportListing(
            new EquityDirectoryListingInput
            {
                Source = "venue-directory",
                SourceIssuerIdentifier = "security:" + Isin,
                IssuerName = "AIRBUS",
                SourceSecurityIdentifier = Isin,
                SourceListingIdentifier = Isin,
                Isin = Isin,
                RelatedIsins = [Isin],
                Ticker = "AIR",
                MarketIdentifierCode = "XPAR",
                MarketCountryCode = "FR",
                TradingCurrency = "EUR",
                QuoteUnitMultiplier = 1m,
                SourceUrl = "https://example.org/venue/securities",
                PayloadJson = "{\"code\":\"AIR\"}",
            }
        );

    [Fact]
    public async Task RecordsTheUsLineAsEvidenceAndWaitsForTheRefresh()
    {
        await using var services = Services();
        await SeedListing(services);
        var reply = new Reply(HttpStatusCode.OK, Answer);
        var importer = Importer(services, reply);

        (await importer.Refresh(CancellationToken.None)).Should().Be(1);
        (await importer.Refresh(CancellationToken.None)).Should().Be(0);

        reply.Bodies.Should().ContainSingle().Which.Should().Contain("\"exchCode\":\"UV\"");
        DbContext.ChangeTracker.Clear();
        var lookup = await DbContext.Set<IsinShareClassLookup>().SingleAsync();
        lookup.ShareClassFigi.Should().Be("BBG001S8TFZ6");
        lookup.UsTickers.Should().Equal("EADSF");
        lookup.UsSecurityType.Should().Be("Common Stock");
        lookup.QuotedOverTheCounter.Should().BeTrue();
        lookup.NextAttemptAt.Should().BeAfter(DateTime.UtcNow.AddDays(29));
        var evidence = await DbContext
            .Set<EquityDirectorySourceRecord>()
            .SingleAsync(row => row.Id == lookup.SourceRecordId);
        evidence.Source.Should().Be(IsinShareClassLookupImporter.EvidenceSource);
        evidence.SourceRecordKey.Should().Be(Isin);
    }

    [Fact]
    public async Task AnOutageKeepsTheIsinDueTheNextDayWithoutEvidence()
    {
        await using var services = Services();
        await SeedListing(services);
        var importer = Importer(services, new Reply(HttpStatusCode.BadRequest, "[]"));

        (await importer.Refresh(CancellationToken.None)).Should().Be(1);

        DbContext.ChangeTracker.Clear();
        var lookup = await DbContext.Set<IsinShareClassLookup>().SingleAsync();
        lookup.SourceRecordId.Should().BeNull();
        lookup.LastError.Should().NotBeNullOrEmpty();
        lookup
            .NextAttemptAt.Should()
            .BeCloseTo(DateTime.UtcNow.AddDays(1), TimeSpan.FromMinutes(5));
    }

    private sealed class Reply(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
