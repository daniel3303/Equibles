using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(HistoricalEquityDbCollection.Name)]
public class NativeListingTickerAliasTests(HistoricalEquityDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    [Fact]
    public async Task Migration_PreservesEveryOriginalSymbolWithoutChangingItsSource()
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        var source = new CommonStock
        {
            Ticker = "ORIGINAL",
            SecondaryTickers = ["SECONDARY"],
            Name = "Original issuer",
        };
        DbContext.Add(source);
        await DbContext.SaveChangesAsync();
        var before = await SourceSnapshot();
        await DbContext.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER equity_listing_ticker_history ON "EquityListing";
            DROP TRIGGER equity_legacy_listing_symbol ON "LegacyEquityListing";
            DROP FUNCTION eq_capture_listing_ticker_alias();
            DROP FUNCTION eq_capture_legacy_listing_symbol();
            DELETE FROM "EquityListingTickerAlias";
            """
        );
        foreach (
            var operation in new PreserveListingTickerAliases().UpOperations.OfType<SqlOperation>()
        )
            await DbContext.Database.ExecuteSqlRawAsync(operation.Sql);
        (await SourceSnapshot()).Should().Be(before);
        (await DbContext.Set<EquityListingTickerAlias>().CountAsync()).Should().Be(2);
        (
            await DbContext
                .Database.SqlQueryRaw<int>(
                    """
                    SELECT count(*)::integer AS "Value" FROM "LegacyEquityListing" m
                    LEFT JOIN "EquityListingTickerAlias" a ON a."EquityListingId" = m."EquityListingId" AND a."Ticker" = m."ListedTicker"
                    WHERE a."EquityListingId" IS NULL
                    """
                )
                .SingleAsync()
        ).Should().Be(0);
        var repository = new EquityListingRepository(DbContext);
        var symbols = await repository.GetRecordedUsSymbols([source.Id]).ToListAsync();
        symbols.Select(row => row.Ticker).Should().BeEquivalentTo(["ORIGINAL", "SECONDARY"]);
        symbols.Select(row => row.EquityListingId).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task Renames_RetainTheExactListingAndItsPreviousSymbols()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "BEFORE");
        var listing = issuer.Presentation.Listing;
        DbContext.Add(issuer);
        await DbContext.SaveChangesAsync();
        listing.Ticker = "MIDDLE";
        await DbContext.SaveChangesAsync();
        listing.Ticker = "AFTER";
        await DbContext.SaveChangesAsync();
        var repository = new EquityListingRepository(DbContext);
        foreach (var ticker in new[] { "BEFORE", "MIDDLE", "AFTER" })
        {
            (await repository.GetByRecordedUsSymbol(issuer.Id, ticker).SingleAsync())
                .Id.Should()
                .Be(listing.Id);
            (
                await new EquityIssuerRepository(DbContext).GetRecordedEquityListingId(
                    issuer.Id,
                    ticker
                )
            )
                .Should()
                .Be(listing.Id);
        }
        var symbols = await repository.GetRecordedUsSymbols([issuer.Id]).ToListAsync();
        symbols.Select(row => row.Ticker).Should().BeEquivalentTo(["BEFORE", "MIDDLE", "AFTER"]);
        (
            await DbContext
                .Set<EquityListingTickerAlias>()
                .Select(row => row.EvidenceSource)
                .Distinct()
                .SingleAsync()
        )
            .Should()
            .Be("listing-symbol-change");
        (await DbContext.Set<LegacyEquityListing>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RecordedSymbols_RefuseAmbiguousUsListingsAndExcludeForeignCollisions()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "BEFORE");
        var listing = issuer.Presentation.Listing;
        DbContext.Add(issuer);
        await DbContext.SaveChangesAsync();
        listing.Ticker = "AFTER";
        DbContext.Add(
            new EquityListing
            {
                Security = listing.Security,
                Ticker = "BEFORE",
                MarketCountryCode = "PT",
                MarketIdentifierCode = "XLIS",
            }
        );
        await DbContext.SaveChangesAsync();
        var issuers = new EquityIssuerRepository(DbContext);
        (await issuers.GetRecordedEquityListingId(issuer.Id, "BEFORE")).Should().Be(listing.Id);
        DbContext.Add(
            new EquityListing
            {
                Security = listing.Security,
                Ticker = "BEFORE",
                MarketCountryCode = "US",
                MarketIdentifierCode = "XNYS",
            }
        );
        await DbContext.SaveChangesAsync();
        (await issuers.GetRecordedEquityListingId(issuer.Id, "BEFORE")).Should().BeNull();
        var current = await DbContext
            .Set<EquityListing>()
            .SingleAsync(row => row.Ticker == "BEFORE" && row.MarketCountryCode == "US");
        (await issuers.GetEquityListingId(issuer.Id, "BEFORE")).Should().Be(current.Id);
        current.Id.Should().NotBe(listing.Id);
        (
            await new EquityListingRepository(DbContext)
                .GetRecordedUsSymbols([issuer.Id])
                .Where(row => row.Ticker == "BEFORE")
                .ToListAsync()
        )
            .Should()
            .HaveCount(2);
    }

    private Task<string> SourceSnapshot() =>
        DbContext
            .Database.SqlQueryRaw<string>(
                """
                SELECT jsonb_build_object(
                    'issuers', (SELECT jsonb_agg(to_jsonb(r) ORDER BY "Id") FROM "CommonStock" r),
                    'mappings', (SELECT jsonb_agg(to_jsonb(r) ORDER BY "EquityListingId") FROM "LegacyEquityListing" r),
                    'listings', (SELECT jsonb_agg(to_jsonb(r) ORDER BY "Id") FROM "EquityListing" r)
                )::text AS "Value"
                """
            )
            .SingleAsync();
}
