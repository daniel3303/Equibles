using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class NativeDirectoryWriteTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    [Fact]
    public async Task HistoricalSeeder_CannotMoveTheRetainedSecurityCusipToTheNewPresentation()
    {
        const string cusip = "037833100";
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "OLD",
            Name: "Issuer",
            Cik: "123",
            Cusip: cusip
        );
        UsEquityDirectory.ReplaceDirectorySymbols(issuer, "NEW", [], activate: true);
        var settlement = new DateOnly(2020, 6, 30);
        var sweep = new DateTime(2026, 9, 12, 1, 0, 0, DateTimeKind.Utc);
        var evidence = new EquityListingRetirementEvidence
        {
            EquityIssuerId = issuer.Id,
            ListedTicker = "NEW",
            DelistedOn = settlement,
            HistoricalCusipBackfillCandidateOn = settlement,
            HistoricalCusipBackfillCandidates = [cusip],
            HistoricalCusipBackfillSweepStartedAt = sweep,
        };
        DbContext.AddRange(issuer, evidence);
        await DbContext.SaveChangesAsync();
        var manager = new Equibles.CommonStocks.BusinessLogic.EquityIdentityManager(
            new EquityIssuerRepository(DbContext),
            NSubstitute.Substitute.For<MassTransit.IBus>()
        );

        var result = await manager.SeedDelistedListingCusip(evidence.Id, cusip, settlement, sweep);

        result
            .Should()
            .Be(Equibles.CommonStocks.BusinessLogic.DelistedListingCusipSeedResult.Ambiguous);
        issuer.Presentation.Listing.Security.Cusip.Should().BeNull();
        (await DbContext.Set<EquitySecurity>().CountAsync(security => security.Cusip == cusip))
            .Should()
            .Be(1);
        evidence.Cusip.Should().BeNull();
    }

    [Fact]
    public async Task RetainedSecurityCusip_CannotBeClaimedAfterPresentationChanges()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "OLD",
            Name: "Issuer",
            Cik: "123",
            Cusip: "037833100"
        );
        var other = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "OTHER",
            Name: "Other",
            Cik: "456",
            Cusip: "037833101",
            SecondaryTickers: ["OTHER-B"]
        );
        var securityId = issuer.Presentation.Listing.EquitySecurityId;
        DbContext.AddRange(issuer, other);
        await DbContext.SaveChangesAsync();
        UsEquityDirectory.ReplaceDirectorySymbols(issuer, "NEW", [], activate: true);
        await DbContext.SaveChangesAsync();
        var repository = new EquityIssuerRepository(DbContext);
        var manager = new Equibles.CommonStocks.BusinessLogic.EquityIdentityManager(
            repository,
            NSubstitute.Substitute.For<MassTransit.IBus>()
        );

        (await manager.SetCusip(other, "037833100")).Should().BeFalse();
        (await manager.SetCusip(issuer, "037833100")).Should().BeFalse();
        (await manager.RecordRetiredCusipAliases(other, ["037833100"])).Should().Be(0);
        (await manager.RecordListedTickerCusips(other, [("OTHER-B", "037833100")])).Should().Be(0);
        var cusipOwner = await repository
            .GetSecurities()
            .SingleAsync(security => security.Cusip == "037833100");
        cusipOwner.Id.Should().Be(securityId);
        cusipOwner.EquityIssuerId.Should().Be(issuer.Id);
        (
            await new Equibles.Sec.Repositories.NportFilingRepository(DbContext).HasCusipIdentity(
                issuer,
                "OLD"
            )
        )
            .Should()
            .BeTrue();
        (
            await new Equibles.Sec.Repositories.NportFilingRepository(DbContext).HasCusipIdentity(
                issuer,
                "NEW"
            )
        )
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task NewPrimary_PreservesOldListingHistory_AndForeignVenue()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "OLD",
            Name: "Issuer",
            Cik: "123"
        );
        var old = issuer.Presentation.Listing;
        var foreign = AddForeignListing(issuer);
        var price = new EquityDailyStockPrice
        {
            Listing = old,
            SourceTicker = "OLD",
            Date = new DateOnly(2020, 1, 2),
            Close = 12.3456m,
            Volume = 123,
        };
        DbContext.AddRange(issuer, price);
        await DbContext.SaveChangesAsync();
        var oldId = old.Id;
        var oldSecurityId = old.EquitySecurityId;
        var foreignBefore = await ListingSnapshot(foreign.Id);

        UsEquityDirectory.ReplaceDirectorySymbols(issuer, "NEW", [], activate: true);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var reloaded = await new EquityIssuerRepository(DbContext).Get(issuer.Id);
        reloaded.Presentation.Listing.Ticker.Should().Be("NEW");
        reloaded.Presentation.EquityListingId.Should().NotBe(oldId);
        var retained = await DbContext.Set<EquityListing>().SingleAsync(row => row.Id == oldId);
        retained.Ticker.Should().Be("OLD");
        retained.EquitySecurityId.Should().Be(oldSecurityId);
        retained.Active.Should().BeFalse();
        var stored = await DbContext.Set<EquityDailyStockPrice>().SingleAsync();
        stored.Id.Should().Be(price.Id);
        stored.EquityListingId.Should().Be(oldId);
        stored.Close.Should().Be(12.3456m);
        (await ListingSnapshot(foreign.Id)).Should().Be(foreignBefore);
    }

    [Fact]
    public async Task FailedDirectorySave_RestoresTheGraph_BeforeAnUnrelatedSave()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "OLD",
            Name: "Original issuer",
            Cik: "123"
        );
        AddForeignListing(issuer);
        var other = new EquityIssuer { Name = "Other", Cik = "456" };
        DbContext.AddRange(issuer, other);
        await DbContext.SaveChangesAsync();
        var originalListingId = issuer.Presentation.EquityListingId;
        var before = await IssuerSnapshot(issuer.Id);
        var snapshot = new EquityDirectorySnapshot(DbContext, issuer);

        UsEquityDirectory.ReplaceDirectorySymbols(issuer, "NEW", ["SECONDARY"], activate: true);
        issuer.Name = "Uncommitted name";
        issuer.Cik = other.Cik;
        var save = async () => await DbContext.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateException>();
        snapshot.Restore();
        other.Name = "Independent successful edit";
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        (await IssuerSnapshot(issuer.Id)).Should().Be(before);
        (await DbContext.Set<EquityIssuerPresentation>().SingleAsync())
            .EquityListingId.Should()
            .Be(originalListingId);
        (await DbContext.Set<EquityListing>().CountAsync()).Should().Be(2);
        (await DbContext.Set<EquitySecurity>().CountAsync()).Should().Be(2);
        (await DbContext.Set<EquityIssuer>().SingleAsync(row => row.Id == other.Id))
            .Name.Should()
            .Be("Independent successful edit");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectoryLock_RefusesPendingChildChangesWithoutRefreshingTheIssuer(
        bool securityChange
    )
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "SAME",
            Name: "Original",
            Cik: "123"
        );
        DbContext.Add(issuer);
        await DbContext.SaveChangesAsync();
        await DbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"EquityIssuer\" SET \"Name\" = 'Concurrent stored name' WHERE \"Id\" = {issuer.Id}"
        );
        if (securityChange)
            issuer.Presentation.Listing.Security.Cusip = "037833100";
        else
            issuer.Presentation.Listing.Active = false;
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        var refresh = () => new EquityIssuerRepository(DbContext).GetForUpdate(issuer.Id);

        await refresh
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*pending Modified*");

        issuer.Name.Should().Be("Original");
        if (securityChange)
            issuer.Presentation.Listing.Security.Cusip.Should().Be("037833100");
        else
            issuer.Presentation.Listing.Active.Should().BeFalse();
    }

    [Theory]
    [InlineData("PT", 1)]
    [InlineData("US", 0)]
    public async Task SymbolReaders_SeparateForeignVenues_AndRefuseAnAmbiguousUsSymbol(
        string otherCountry,
        int expectedSymbolRows
    )
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "SAME", Name: "Issuer");
        var first = issuer.Presentation.Listing;
        first.MarketIdentifierCode = "XNYS";
        var second = AddForeignListing(issuer);
        second.Ticker = "SAME";
        second.MarketCountryCode = otherCountry;
        second.MarketIdentifierCode = otherCountry == "US" ? "XNAS" : "XLIS";
        var date = new DateOnly(2026, 8, 3);
        DbContext.Add(issuer);
        foreach (var listing in new[] { first, second })
        {
            DbContext.Add(
                new EquityDailyStockPrice
                {
                    Listing = listing,
                    SourceTicker = "SAME",
                    Date = date,
                    Close = listing == first ? 10m : 20m,
                    Volume = 100,
                }
            );
            DbContext.Add(
                new Equibles.Finra.Data.Models.DailyShortVolume
                {
                    Listing = listing,
                    ListedTicker = "SAME",
                    Date = date,
                    ShortVolume = 1,
                    TotalVolume = 2,
                }
            );
            DbContext.Add(
                new Equibles.Finra.Data.Models.ShortInterest
                {
                    Listing = listing,
                    ListedTicker = "SAME",
                    SettlementDate = date,
                    CurrentShortPosition = 100,
                }
            );
            DbContext.Add(
                new Equibles.Finra.Data.Models.OffExchangeVolume
                {
                    Listing = listing,
                    ListedTicker = "SAME",
                    WeekStartDate = date,
                }
            );
            DbContext.Add(
                new Equibles.Sec.Data.Models.FailToDeliver
                {
                    Listing = listing,
                    ListedTicker = "SAME",
                    SettlementDate = date,
                    Quantity = 100,
                }
            );
        }
        await DbContext.SaveChangesAsync();
        (
            await new Equibles.Finra.Repositories.DailyShortVolumeRepository(DbContext)
                .GetHistoryByListing(issuer, "SAME")
                .CountAsync()
        )
            .Should()
            .Be(expectedSymbolRows);
        (
            await new Equibles.Finra.Repositories.ShortInterestRepository(DbContext)
                .GetHistoryByListing(issuer, "SAME")
                .CountAsync()
        )
            .Should()
            .Be(expectedSymbolRows);
        (
            await new Equibles.Finra.Repositories.OffExchangeVolumeRepository(DbContext)
                .GetHistoryByListing(issuer, "SAME")
                .CountAsync()
        )
            .Should()
            .Be(expectedSymbolRows);
        (
            await new Equibles.Sec.Repositories.FailToDeliverRepository(DbContext)
                .GetByListing(issuer, "SAME")
                .CountAsync()
        )
            .Should()
            .Be(expectedSymbolRows);
        (
            await new Equibles.Yahoo.Repositories.EquityDailyStockPriceRepository(DbContext)
                .GetUsSeries(issuer.Id, "SAME")
                .CountAsync()
        )
            .Should()
            .Be(expectedSymbolRows);
        var prices = await new Equibles.Yahoo.HostedService.Services.YahooStockPriceProvider(
            DbContext
        ).GetClosingPrices([(issuer.Id, (string)null, date)]);
        prices[(issuer.Id, null, date)].Should().Be(10m);
        (await DbContext.Set<EquityDailyStockPrice>().CountAsync()).Should().Be(2);
    }

    private static EquityListing AddForeignListing(EquityIssuer issuer)
    {
        var security = new EquitySecurity
        {
            Issuer = issuer,
            EquityIssuerId = issuer.Id,
            Isin = "PTEDP0AM0009",
        };
        var listing = new EquityListing
        {
            Security = security,
            EquitySecurityId = security.Id,
            Ticker = "OLD",
            MarketCountryCode = "PT",
            MarketIdentifierCode = "XLIS",
            TradingCurrency = "EUR",
            QuoteUnitMultiplier = 1m,
        };
        security.Listings.Add(listing);
        issuer.Securities.Add(security);
        return listing;
    }

    private Task<string> ListingSnapshot(Guid id) =>
        DbContext
            .Database.SqlQuery<string>(
                $"SELECT to_jsonb(row)::text AS \"Value\" FROM \"EquityListing\" row WHERE \"Id\" = {id}"
            )
            .SingleAsync();

    private Task<string> IssuerSnapshot(Guid id) =>
        DbContext
            .Database.SqlQuery<string>(
                $"""
                SELECT jsonb_build_object(
                    'issuer', (SELECT to_jsonb(row) FROM "EquityIssuer" row WHERE "Id" = {id}),
                    'securities', (SELECT jsonb_agg(to_jsonb(row) ORDER BY "Id") FROM "EquitySecurity" row WHERE "EquityIssuerId" = {id}),
                    'listings', (SELECT jsonb_agg(to_jsonb(listing) ORDER BY listing."Id") FROM "EquityListing" listing JOIN "EquitySecurity" security ON security."Id" = listing."EquitySecurityId" WHERE security."EquityIssuerId" = {id}),
                    'presentation', (SELECT to_jsonb(row) FROM "EquityIssuerPresentation" row WHERE "EquityIssuerId" = {id})
                )::text AS "Value"
                """
            )
            .SingleAsync();
}
