using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Equibles.IntegrationTests.CorporateActions;

[Collection(HistoricalEquityDbCollection.Name)]
public class NativeActionAttributionTests(HistoricalEquityDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    private async Task ReplayAttribution()
    {
        var sql = new AttributeCorporateActionsToListings()
            .UpOperations.OfType<SqlOperation>()
            .Single()
            .Sql;
        await DbContext.Database.ExecuteSqlRawAsync(
            sql[sql.IndexOf("WITH candidates AS", StringComparison.Ordinal)..]
        );
        DbContext.ChangeTracker.Clear();
    }

    private static StockSplit Split(Guid issuerId, string ticker, int day = 1) =>
        new()
        {
            EquityIssuerId = issuerId,
            PriceSeriesTicker = ticker,
            EffectiveDate = new DateOnly(2025, 1, day),
            Numerator = 2,
            Denominator = 1,
            Source = StockSplitSource.Yahoo,
            PriceAdjustmentAppliedTime = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        };

    [Fact]
    public async Task Backfill_UsesExactRecordedSymbols_AndPreservesEveryOriginalField()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "CURRENT");
        DbContext.Add(issuer);
        await DbContext.SaveChangesAsync();
        var listingId = issuer.Presentation.EquityListingId;
        issuer.Presentation.Listing.Ticker = "RENAMED";
        await DbContext.SaveChangesAsync();
        var known = Split(issuer.Id, "CURRENT");
        var unknown = Split(issuer.Id, null, 2);
        var absent = Split(issuer.Id, "UNRESOLVED", 3);
        DbContext.AddRange(known, unknown, absent);
        DbContext.Add(
            new CashDividend
            {
                EquityIssuerId = issuer.Id,
                ExDate = new DateOnly(2025, 1, 1),
                AmountPerShare = 1.25m,
                Source = CashDividendSource.Yahoo,
            }
        );
        await DbContext.SaveChangesAsync();
        var before = await SourceRows();
        await ReplayAttribution();
        (await SourceRows()).Should().Be(before);
        (await DbContext.Set<StockSplit>().SingleAsync(row => row.Id == known.Id))
            .EquityListingId.Should()
            .Be(listingId);
        (await DbContext.Set<StockSplit>().CountAsync(row => row.EquityListingId == null))
            .Should()
            .Be(2);
        var dividend = await DbContext.Set<CashDividend>().SingleAsync();
        dividend.EquityListingId.Should().BeNull();
        dividend.Currency.Should().BeNull();
        await ReplayAttribution();
        (await SourceRows()).Should().Be(before);
    }

    [Fact]
    public async Task Backfill_KeepsCollidingAliasEventsAndAmbiguousListingsUnattributed()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "OLD");
        DbContext.Add(issuer);
        await DbContext.SaveChangesAsync();
        issuer.Presentation.Listing.Ticker = "CURRENT";
        await DbContext.SaveChangesAsync();
        DbContext.AddRange(Split(issuer.Id, "OLD"), Split(issuer.Id, "CURRENT"));
        var second = new EquityListing
        {
            Security = issuer.Presentation.Listing.Security,
            Ticker = "CURRENT",
            MarketCountryCode = "US",
            MarketIdentifierCode = "XNAS",
        };
        DbContext.Add(second);
        DbContext.Add(Split(issuer.Id, "CURRENT", 2));
        await DbContext.SaveChangesAsync();
        var before = await SourceRows();
        await ReplayAttribution();
        (await SourceRows()).Should().Be(before);
        // OLD uniquely resolves; CURRENT is ambiguous and must not be guessed into either venue.
        (await DbContext.Set<StockSplit>().CountAsync(row => row.EquityListingId == null))
            .Should()
            .Be(2);
    }

    [Fact]
    public async Task Backfill_DoesNotMergeTwoRecordedSymbolsForOneListingAndDate()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "OLD");
        DbContext.Add(issuer);
        await DbContext.SaveChangesAsync();
        issuer.Presentation.Listing.Ticker = "CURRENT";
        await DbContext.SaveChangesAsync();
        DbContext.AddRange(Split(issuer.Id, "OLD"), Split(issuer.Id, "CURRENT"));
        await DbContext.SaveChangesAsync();
        var before = await SourceRows();
        await ReplayAttribution();
        (await SourceRows()).Should().Be(before);
        (await DbContext.Set<StockSplit>().CountAsync(row => row.EquityListingId == null))
            .Should()
            .Be(2);
    }

    [Theory]
    [InlineData("listing-security")]
    [InlineData("security-issuer")]
    [InlineData("currency")]
    [InlineData("action-listing")]
    public async Task RecordedNonPrimaryActions_BlockReverseIdentityChanges(string scenario)
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "PRIMARY");
        var other = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "OTHER");
        var security = new EquitySecurity { Issuer = issuer };
        var listing = new EquityListing
        {
            Security = security,
            Ticker = "SECONDARY",
            MarketCountryCode = "PT",
            MarketIdentifierCode = "XLIS",
            TradingCurrency = "EUR",
        };
        var sibling = new EquityListing
        {
            Security = security,
            Ticker = "SECONDARY",
            MarketCountryCode = "PT",
            MarketIdentifierCode = "ENXL",
            TradingCurrency = "EUR",
        };
        DbContext.AddRange(issuer, other, listing, sibling);
        await DbContext.SaveChangesAsync();
        var split = Split(issuer.Id, listing.Ticker);
        split.EquityListingId = listing.Id;
        DbContext.Add(split);
        DbContext.Add(
            new CashDividend
            {
                EquityIssuerId = issuer.Id,
                EquityListingId = listing.Id,
                Currency = "EUR",
                ExDate = new DateOnly(2025, 1, 1),
                AmountPerShare = 1,
            }
        );
        await DbContext.SaveChangesAsync();
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        await transaction.CreateSavepointAsync("before_change");
        if (scenario == "listing-security")
            listing.EquitySecurityId = issuer.Presentation.Listing.EquitySecurityId;
        if (scenario == "security-issuer")
            security.EquityIssuerId = other.Id;
        if (scenario == "currency")
            listing.TradingCurrency = "USD";
        if (scenario == "action-listing")
            split.EquityListingId = sibling.Id;
        var save = () => DbContext.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateException>();
        await transaction.RollbackToSavepointAsync("before_change");
        DbContext.ChangeTracker.Clear();
        var retained = await DbContext.Set<StockSplit>().SingleAsync();
        retained.EquityListingId.Should().Be(listing.Id);
        retained.PriceAdjustmentAppliedTime.Should().NotBeNull();
        (await DbContext.Set<EquityListing>().SingleAsync(row => row.Id == listing.Id))
            .TradingCurrency.Should()
            .Be("EUR");
        (await DbContext.Set<EquitySecurity>().SingleAsync(row => row.Id == security.Id))
            .EquityIssuerId.Should()
            .Be(issuer.Id);
    }

    [Fact]
    public async Task SameIssuerAndSymbol_OnTwoVenuesKeepSeparateActionsAndDenominations()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "SAME");
        var us = issuer.Presentation.Listing;
        us.TradingCurrency = "USD";
        var pt = new EquityListing
        {
            Ticker = "SAME",
            MarketCountryCode = "PT",
            MarketIdentifierCode = "XLIS",
            TradingCurrency = "EUR",
            Security = us.Security,
        };
        DbContext.AddRange(issuer, pt);
        await DbContext.SaveChangesAsync();
        foreach (var listing in new[] { us, pt })
        {
            var split = Split(issuer.Id, listing.Ticker);
            split.EquityListingId = listing.Id;
            DbContext.Add(split);
            DbContext.Add(
                new CashDividend
                {
                    EquityIssuerId = issuer.Id,
                    EquityListingId = listing.Id,
                    Currency = listing.TradingCurrency,
                    ExDate = new DateOnly(2025, 1, 1),
                    AmountPerShare = listing == us ? 1m : 2m,
                }
            );
        }
        DbContext.Add(
            new CashDividend
            {
                EquityIssuerId = issuer.Id,
                ExDate = new DateOnly(2025, 1, 1),
                AmountPerShare = 3m,
            }
        );
        await DbContext.SaveChangesAsync();
        var dividends = new CashDividendRepository(DbContext);
        (await dividends.GetByListing(us.Id).SingleAsync()).AmountPerShare.Should().Be(1);
        (await dividends.GetByListing(pt.Id).SingleAsync()).Currency.Should().Be("EUR");
        (await new StockSplitRepository(DbContext).GetByListing(pt.Id).CountAsync()).Should().Be(1);
        (await DbContext.Set<CashDividend>().CountAsync()).Should().Be(3);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("currency")]
    [InlineData("missing-currency")]
    [InlineData("symbol")]
    [InlineData("erase-listing")]
    public async Task NativeGuards_RefuseConflictingAttributionWithoutLosingRows(string scenario)
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "VALID");
        issuer.Presentation.Listing.TradingCurrency = "USD";
        var other = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "OTHER");
        DbContext.AddRange(issuer, other);
        await DbContext.SaveChangesAsync();
        var split = Split(issuer.Id, "VALID");
        split.EquityListingId = issuer.Presentation.EquityListingId;
        DbContext.Add(split);
        await DbContext.SaveChangesAsync();
        var before = await SourceRows();
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        await transaction.CreateSavepointAsync("before_invalid");
        if (scenario == "owner")
            split.EquityIssuerId = other.Id;
        if (scenario == "symbol")
            split.PriceSeriesTicker = "OTHER";
        if (scenario == "erase-listing")
            split.EquityListingId = null;
        if (scenario is "currency" or "missing-currency")
            DbContext.Add(
                new CashDividend
                {
                    EquityIssuerId = issuer.Id,
                    EquityListingId = issuer.Presentation.EquityListingId,
                    Currency = scenario == "currency" ? "EUR" : null,
                    ExDate = new DateOnly(2025, 1, 1),
                    AmountPerShare = 1m,
                }
            );
        var save = () => DbContext.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateException>();
        await transaction.RollbackToSavepointAsync("before_invalid");
        DbContext.ChangeTracker.Clear();
        (await SourceRows()).Should().Be(before);
    }

    private Task<string> SourceRows() =>
        DbContext
            .Database.SqlQueryRaw<string>(
                """
                SELECT jsonb_build_object(
                    'splits', (SELECT jsonb_agg(to_jsonb(row) - 'EquityListingId' ORDER BY "Id") FROM "StockSplit" row),
                    'dividends', (SELECT jsonb_agg(to_jsonb(row) - 'EquityListingId' - 'Currency' ORDER BY "Id") FROM "CashDividend" row)
                )::text AS "Value"
                """
            )
            .SingleAsync();
}
