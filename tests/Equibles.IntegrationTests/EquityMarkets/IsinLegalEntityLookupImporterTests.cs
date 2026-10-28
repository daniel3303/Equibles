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
                Source = "venue-directory",
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
                SourceUrl = "https://example.org/venue/securities",
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
                    SourceUrl = new Uri(
                        "https://api.gleif.org/api/v1/lei-records?filter%5Bisin%5D=" + Isin
                    ),
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
                    ? new InvalidDataException(
                        "GLEIF must identify exactly one issuer for this ISIN."
                    )
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
        lookup
            .NextAttemptAt.Should()
            .BeCloseTo(DateTime.UtcNow.AddDays(days), TimeSpan.FromMinutes(5));
        (await importer.Refresh(CancellationToken.None)).Should().Be(0);
    }

    // GLEIF relates no ISIN of some registers (India's) to their company, so an issuer holding an LEI is
    // answered with the ISINs GLEIF relates to that LEI, such as its American depositary receipt.
    [Fact]
    public async Task AnIssuerWithAnLeiIsAnsweredWithTheIsinsGleifRelatesToThatLei()
    {
        await using var services = Services();
        await SeedListing(services);
        await HoldLei();
        var gleif = Gleif();
        gleif.GetIsinsForLei(Lei, Arg.Any<CancellationToken>()).Returns(LeiAnswer());
        var importer = Importer(services, gleif);

        (await importer.Refresh(CancellationToken.None)).Should().Be(1);
        (await importer.Refresh(CancellationToken.None)).Should().Be(0);

        DbContext.ChangeTracker.Clear();
        var lookup = await DbContext.Set<IsinLegalEntityLookup>().SingleAsync();
        lookup.LegalEntityIdentifier.Should().Be(Lei);
        lookup.RelatedIsins.Should().Equal("US01609W1027");
        lookup.RelatedIsinCount.Should().Be(1);
        lookup.SourceRecordId.Should().NotBeNull();
        lookup.NextAttemptAt.Should().BeAfter(DateTime.UtcNow.AddDays(29));
        await gleif.DidNotReceiveWithAnyArgs().GetIssuerForIsin(default, default);
        (await DbContext.Set<EquityIssuer>().SingleAsync()).LegalEntityIdentifier.Should().Be(Lei);
    }

    // The ISIN was answered before its issuer held an LEI; the LEI's own ISINs are asked for at once rather
    // than at the next monthly refresh.
    [Fact]
    public async Task AnAnswerFromBeforeTheIssuerHeldItsLeiIsAskedAgainByThatLei()
    {
        await using var services = Services();
        await SeedListing(services);
        var gleif = Gleif();
        gleif
            .GetIssuerForIsin(Isin, Arg.Any<CancellationToken>())
            .Returns(new GleifIssuerIdentity { RequestedIsin = Isin });
        gleif.GetIsinsForLei(Lei, Arg.Any<CancellationToken>()).Returns(LeiAnswer());
        var importer = Importer(services, gleif);
        (await importer.Refresh(CancellationToken.None)).Should().Be(1);

        await HoldLei();

        (await importer.Refresh(CancellationToken.None)).Should().Be(1);
        (await importer.Refresh(CancellationToken.None)).Should().Be(0);
        DbContext.ChangeTracker.Clear();
        var lookup = await DbContext.Set<IsinLegalEntityLookup>().SingleAsync();
        (lookup.LegalEntityIdentifier, lookup.RelatedIsins.Single())
            .Should()
            .Be((Lei, "US01609W1027"));
        await gleif.Received(1).GetIsinsForLei(Lei, Arg.Any<CancellationToken>());
    }

    // An answer asked by the LEI never lists the row's own ISIN, so it is not GLEIF's mapping of that ISIN;
    // once the issuer no longer holds the LEI the ISIN is asked again by itself.
    [Fact]
    public async Task AnLeiAnswerIsAskedAgainByIsinOnceTheIssuerNoLongerHoldsThatLei()
    {
        await using var services = Services();
        await SeedListing(services);
        await HoldLei();
        var gleif = Gleif();
        gleif.GetIsinsForLei(Lei, Arg.Any<CancellationToken>()).Returns(LeiAnswer());
        gleif
            .GetIssuerForIsin(Isin, Arg.Any<CancellationToken>())
            .Returns(new GleifIssuerIdentity { RequestedIsin = Isin });
        var importer = Importer(services, gleif);
        (await importer.Refresh(CancellationToken.None)).Should().Be(1);

        await HoldLei(null);

        (await importer.Refresh(CancellationToken.None)).Should().Be(1);
        (await importer.Refresh(CancellationToken.None)).Should().Be(0);
        DbContext.ChangeTracker.Clear();
        var lookup = await DbContext.Set<IsinLegalEntityLookup>().SingleAsync();
        (lookup.LegalEntityIdentifier, lookup.RelatedIsins.Count).Should().Be((null, 0));
        await gleif.Received(1).GetIssuerForIsin(Isin, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedLeiAnswerWaitsForItsRetry()
    {
        await using var services = Services();
        await SeedListing(services);
        await HoldLei();
        var gleif = Gleif();
        gleif
            .GetIsinsForLei(Lei, Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Service unavailable"));
        var importer = Importer(services, gleif);

        (await importer.Refresh(CancellationToken.None)).Should().Be(1);
        (await importer.Refresh(CancellationToken.None)).Should().Be(0);

        DbContext.ChangeTracker.Clear();
        var lookup = await DbContext.Set<IsinLegalEntityLookup>().SingleAsync();
        (lookup.LegalEntityIdentifier, lookup.SourceRecordId).Should().Be((null, (Guid?)null));
        lookup
            .NextAttemptAt.Should()
            .BeCloseTo(DateTime.UtcNow.AddDays(1), TimeSpan.FromMinutes(5));
    }

    private async Task HoldLei(string lei = Lei)
    {
        DbContext.ChangeTracker.Clear();
        var issuer = await DbContext.Set<EquityIssuer>().SingleAsync();
        issuer.LegalEntityIdentifier = lei;
        await DbContext.SaveChangesAsync();
    }

    private static GleifIssuerIdentity LeiAnswer() =>
        new()
        {
            RequestedLei = Lei,
            LegalEntityIdentifier = Lei,
            LegalName = "Alibaba Group Holding Limited",
            RelatedIsins = ["US01609W1027"],
            RelatedIsinCount = 1,
            SourceUrl = new Uri("https://api.gleif.org/api/v1/lei-records/" + Lei),
        };

    private static IsinLegalEntityLookupImporter Importer(
        ServiceProvider services,
        GleifIdentityClient gleif
    ) =>
        new(
            services.GetRequiredService<IServiceScopeFactory>(),
            gleif,
            NullLogger<IsinLegalEntityLookupImporter>()
        );
}
