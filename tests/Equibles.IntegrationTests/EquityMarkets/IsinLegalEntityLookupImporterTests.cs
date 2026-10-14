using Equibles.CommonStocks.BusinessLogic.Directory;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.EquityMarkets.BusinessLogic.Directory;
using Equibles.Integrations.Gleif;
using Equibles.Integrations.Gleif.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Equibles.IntegrationTests.EquityMarkets;

[Collection(ParadeDbCollection.Name)]
public class IsinLegalEntityLookupImporterTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    private const string Isin = "KYG017191142";
    private const string Lei = "5493001NTNQJDH60PM02";

    private ServiceProvider Services() =>
        new ServiceCollection()
            .AddScoped<EquiblesFinancialDbContext>(_ => Fixture.CreateDbContext())
            .AddScoped<EquityIssuerRepository>()
            .AddScoped<EquityListingRepository>()
            .AddScoped<EquityDirectorySourceRecordRepository>()
            .AddScoped<IsinLegalEntityLookupRepository>()
            .BuildServiceProvider();

    private static GleifIdentityClient Gleif() =>
        Substitute.For<GleifIdentityClient>(new HttpClient(), NullLogger<GleifIdentityClient>());

    private static async Task<Guid> SeedListing(ServiceProvider services, string isin = Isin)
    {
        var importer = new EquityDirectoryIdentityImporter(
            services.GetRequiredService<IServiceScopeFactory>()
        );
        return await importer.ImportListing(
            new EquityDirectoryListingInput
            {
                Source = "hkex-securities",
                SourceIssuerIdentifier = "security:" + isin,
                IssuerName = "BABA-W",
                SourceSecurityIdentifier = isin,
                SourceListingIdentifier = isin,
                Isin = isin,
                RelatedIsins = [isin],
                Ticker = "09988",
                MarketIdentifierCode = "XHKG",
                MarketCountryCode = "HK",
                TradingCurrency = "HKD",
                QuoteUnitMultiplier = 1m,
                SourceUrl = "https://www.hkex.com.hk/eng/services/trading/securities/securitieslist",
                PayloadJson = "{\"code\":\"09988\"}",
            }
        );
    }

    [Fact]
    public async Task RecordsGleifEvidenceWithoutAssigningTheIssuerLei()
    {
        await using var services = Services();
        await SeedListing(services);
        var gleif = Gleif();
        gleif
            .GetIssuerForIsin(Isin, Arg.Any<CancellationToken>())
            .Returns(
                new GleifIssuerIdentity
                {
                    RequestedIsin = Isin,
                    LegalEntityIdentifier = Lei,
                    LegalName = "Alibaba Group Holding Limited",
                    RelatedIsins = [Isin, "US01609W1027"],
                    RelatedIsinCount = 2,
                    SourceUrl = new Uri("https://api.gleif.org/api/v1/lei-records?filter%5Bisin%5D=" + Isin),
                }
            );
        var importer = new IsinLegalEntityLookupImporter(
            services.GetRequiredService<IServiceScopeFactory>(),
            gleif,
            NullLogger<IsinLegalEntityLookupImporter>()
        );

        (await importer.Refresh(CancellationToken.None)).Should().Be(1);
        (await importer.Refresh(CancellationToken.None)).Should().Be(0);

        DbContext.ChangeTracker.Clear();
        var lookup = await DbContext.Set<IsinLegalEntityLookup>().SingleAsync();
        lookup.LegalEntityIdentifier.Should().Be(Lei);
        lookup.RelatedIsins.Should().Equal(Isin, "US01609W1027");
        lookup.LastError.Should().BeNull();
        lookup.NextAttemptAt.Should().BeAfter(DateTime.UtcNow.AddDays(29));
        var evidence = await DbContext
            .Set<EquityDirectorySourceRecord>()
            .SingleAsync(row => row.Id == lookup.SourceRecordId);
        evidence.Source.Should().Be(IsinLegalEntityLookupImporter.EvidenceSource);
        evidence.SourceRecordKey.Should().Be(Isin);
        (await DbContext.Set<EquityIssuer>().SingleAsync()).LegalEntityIdentifier.Should().BeNull();
        await gleif.Received(1).GetIssuerForIsin(Isin, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 30)]
    public async Task AFailedLookupBacksOffWithoutEvidence(bool answered, int days)
    {
        await using var services = Services();
        await SeedListing(services);
        var gleif = Gleif();
        gleif
            .GetIssuerForIsin(Isin, Arg.Any<CancellationToken>())
            .ThrowsAsync(
                answered
                    ? new InvalidDataException("GLEIF must identify exactly one issuer for this ISIN.")
                    : new HttpRequestException("Service unavailable")
            );
        var importer = new IsinLegalEntityLookupImporter(
            services.GetRequiredService<IServiceScopeFactory>(),
            gleif,
            NullLogger<IsinLegalEntityLookupImporter>()
        );

        (await importer.Refresh(CancellationToken.None)).Should().Be(1);

        DbContext.ChangeTracker.Clear();
        var lookup = await DbContext.Set<IsinLegalEntityLookup>().SingleAsync();
        lookup.SourceRecordId.Should().BeNull();
        lookup.LastError.Should().NotBeNullOrEmpty();
        lookup.NextAttemptAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(days), TimeSpan.FromMinutes(5));
        (await importer.Refresh(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task AnIssuerWithAnLeiIsNeverLookedUp()
    {
        await using var services = Services();
        await SeedListing(services);
        var issuer = await DbContext.Set<EquityIssuer>().SingleAsync();
        issuer.LegalEntityIdentifier = Lei;
        await DbContext.SaveChangesAsync();
        var gleif = Gleif();
        var importer = new IsinLegalEntityLookupImporter(
            services.GetRequiredService<IServiceScopeFactory>(),
            gleif,
            NullLogger<IsinLegalEntityLookupImporter>()
        );

        (await importer.Refresh(CancellationToken.None)).Should().Be(0);
        await gleif.DidNotReceiveWithAnyArgs().GetIssuerForIsin(default, default);
    }
}
