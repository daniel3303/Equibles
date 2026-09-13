using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class NativeDirectoryIdentityTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    [Fact]
    public async Task CountryBackfill_PreservesRows_AndScopesUnqualifiedTickersToUs()
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        var original = new CommonStock { Ticker = "SAME", Name = "U.S. listed foreign issuer" };
        DbContext.Add(original);
        var foreign = new EquityListing
        {
            Ticker = "SAME",
            MarketCountryCode = "PT",
            MarketIdentifierCode = "XLIS",
            Security = new EquitySecurity
            {
                Issuer = new EquityIssuer
                {
                    Name = "Lisbon issuer",
                    LegalEntityIdentifier = "5493001KJTIIGC8Y1R12",
                },
            },
        };
        DbContext.Add(foreign);
        await DbContext.SaveChangesAsync();
        var usId = await DbContext
            .Set<LegacyEquityListing>()
            .Where(row => row.CommonStockId == original.Id)
            .Select(row => row.EquityListingId)
            .SingleAsync();
        await DbContext.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER eq_legacy_listing_market ON "LegacyEquityListing";
            UPDATE "EquityListing" SET "MarketCountryCode" = NULL WHERE "MarketCountryCode" = 'US';
            """
        );
        var before = await Snapshot();
        // Replay the actual cohort backfill and rolling-write guard against existing rows.
        foreach (
            var operation in new AddNativeDirectoryIdentity().UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>()
        )
            await DbContext.Database.ExecuteSqlRawAsync(operation.Sql);
        (await Snapshot()).Should().Be(before);
        var listings = new EquityListingRepository(DbContext);
        (await listings.GetUsByTicker("SAME").Select(row => row.Id).ToListAsync())
            .Should()
            .Equal(usId);
        (
            await DbContext
                .Set<EquityListing>()
                .Where(row => row.Id == foreign.Id)
                .Select(row => row.MarketCountryCode)
                .SingleAsync()
        )
            .Should()
            .Be("PT");
        (
            await new EquityIssuerRepository(DbContext)
                .GetByLegalEntityIdentifier("5493001KJTIIGC8Y1R12")
                .Select(row => row.Id)
                .SingleAsync()
        )
            .Should()
            .Be(foreign.Security.Issuer.Id);
        var subsequent = new CommonStock { Ticker = "LATER", Name = "Later U.S. directory row" };
        DbContext.Add(subsequent);
        await DbContext.SaveChangesAsync();
        (await listings.GetUsByTicker("LATER").CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task LegacyMapping_RejectsForeignListing_WithoutChangingItsIdentity()
    {
        var listing = new EquityListing
        {
            Ticker = "LISBON",
            MarketCountryCode = "PT",
            Security = new EquitySecurity { Issuer = new EquityIssuer { Name = "Lisbon issuer" } },
        };
        DbContext.Add(listing);
        await DbContext.SaveChangesAsync();
        DbContext.Add(
            new LegacyEquityListing
            {
                CommonStockId = listing.Security.Issuer.Id,
                ListedTicker = listing.Ticker,
                EquityListingId = listing.Id,
            }
        );
        var save = async () => await DbContext.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateException>();
        DbContext.ChangeTracker.Clear();
        (
            await DbContext
                .Set<EquityListing>()
                .Where(row => row.Id == listing.Id)
                .Select(row => row.MarketCountryCode)
                .SingleAsync()
        )
            .Should()
            .Be("PT");
        (await DbContext.Set<LegacyEquityListing>().CountAsync()).Should().Be(0);
    }

    private Task<string> Snapshot() =>
        DbContext
            .Database.SqlQueryRaw<string>(
                """
                SELECT jsonb_build_object(
                  'issuers', (SELECT jsonb_agg(to_jsonb(row) ORDER BY "Id") FROM "EquityIssuer" row),
                  'securities', (SELECT jsonb_agg(to_jsonb(row) ORDER BY "Id") FROM "EquitySecurity" row),
                  'listings', (SELECT jsonb_agg(to_jsonb(row) - 'MarketCountryCode' ORDER BY "Id") FROM "EquityListing" row),
                  'mapping', (SELECT jsonb_agg(to_jsonb(row) ORDER BY "EquityListingId") FROM "LegacyEquityListing" row)
                )::text AS "Value"
                """
            )
            .SingleAsync();
}
