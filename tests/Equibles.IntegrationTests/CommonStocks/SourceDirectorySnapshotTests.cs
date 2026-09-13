using System.Text.Json;
using Equibles.CommonStocks.BusinessLogic.Directory;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class SourceDirectorySnapshotTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    private ServiceProvider Services() =>
        new ServiceCollection()
            .AddScoped<EquiblesFinancialDbContext>(_ => Fixture.CreateDbContext())
            .AddScoped<EquityIssuerRepository>()
            .AddScoped<EquityListingRepository>()
            .AddScoped<EquityDirectorySourceRecordRepository>()
            .BuildServiceProvider();

    private static EquityDirectoryListingInput Listing(
        string isin = "PTALT0AE0002",
        string ticker = "SAME"
    ) =>
        new()
        {
            Source = "euronext",
            SourceIssuerIdentifier = isin,
            IssuerName = "Source issuer " + isin,
            Isin = isin,
            RelatedIsins = [isin],
            Ticker = ticker,
            MarketIdentifierCode = "XLIS",
            MarketCountryCode = "PT",
            TradingCurrency = "EUR",
            QuoteUnitMultiplier = 1m,
            SourceUrl = $"https://live.euronext.com/en/product/equities/{isin}-XLIS",
            PayloadJson = JsonSerializer.Serialize(new { isin, ticker }),
        };

    private static EquityDirectorySnapshotInput Snapshot(
        int revision,
        params EquityDirectoryListingInput[] listings
    ) =>
        new()
        {
            Source = "euronext",
            EvidenceSource = "euronext-lisbon-directory-v1",
            SourceRecordKey = "https://live.euronext.com/en/markets/lisbon/equities/list",
            PayloadJson = JsonSerializer.Serialize(new { revision, listings }),
            ObservedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(revision),
            MarketCountryCode = "PT",
            MarketIdentifierCodes = ["XLIS", "ENXL", "ALXL"],
            Listings = listings
                .Select(row => new EquityDirectoryListingKey(
                    row.Isin,
                    row.MarketIdentifierCode,
                    row.Ticker
                ))
                .ToList(),
        };

    [Fact]
    public async Task ReassignedSymbolRevokesOldCaptureBeforeNewProductImportAndKeepsItsPrices()
    {
        await using var services = Services();
        var factory = services.GetRequiredService<IServiceScopeFactory>();
        var snapshots = new EquityDirectorySnapshotManager(factory);
        var importer = new EquityDirectoryIdentityImporter(factory);
        var old = Listing();
        old.DirectorySnapshotId = await snapshots.Reconcile(Snapshot(0, old));
        var oldId = await importer.ImportListing(old);
        var us = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "SAME", Name: "U.S. issuer");
        DbContext.Add(us);
        var price = new EquityDailyStockPrice
        {
            EquityListingId = oldId,
            SourceTicker = "SAME",
            Date = new DateOnly(2026, 8, 31),
            Open = 12m,
            High = 12m,
            Low = 12m,
            Close = 12m,
            AdjustedClose = 12m,
            Volume = 100,
        };
        DbContext.Add(price);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var replacement = Listing("PTEDP0AM0009");
        replacement.DirectorySnapshotId = await snapshots.Reconcile(Snapshot(1, replacement));
        var beforeImport = await DbContext
            .Set<EquityListing>()
            .AsNoTracking()
            .SingleAsync(row => row.Id == oldId);
        beforeImport.Active.Should().BeFalse();
        beforeImport.IsDirectoryListed.Should().BeFalse();
        beforeImport
            .DelistedOn.Should()
            .BeNull("absence does not establish an effective delisting date");
        var newId = await importer.ImportListing(replacement);
        newId.Should().NotBe(oldId);
        var stale = () => importer.ImportListing(old);
        await stale.Should().ThrowAsync<InvalidDataException>();
        var rows = await DbContext.Set<EquityDailyStockPrice>().AsNoTracking().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Id.Should().Be(price.Id);
        rows[0].EquityListingId.Should().Be(oldId);
        rows[0].Close.Should().Be(12m);
        (
            await DbContext
                .Set<EquityListing>()
                .AsNoTracking()
                .SingleAsync(row => row.Id == us.Presentation.EquityListingId)
        )
            .Active.Should()
            .BeTrue();
        (await DbContext.Set<EquityListing>().AsNoTracking().SingleAsync(row => row.Id == newId))
            .Active.Should()
            .BeTrue();
    }

    [Fact]
    public async Task SameSecurityReappearingWithoutAnEffectiveRetirementKeepsItsListingIdentity()
    {
        await using var services = Services();
        var factory = services.GetRequiredService<IServiceScopeFactory>();
        var snapshots = new EquityDirectorySnapshotManager(factory);
        var importer = new EquityDirectoryIdentityImporter(factory);
        var original = Listing();
        original.DirectorySnapshotId = await snapshots.Reconcile(Snapshot(0, original));
        var id = await importer.ImportListing(original);
        var other = Listing("PTEDP0AM0009", "OTHER");
        await snapshots.Reconcile(Snapshot(1, other));
        original.DirectorySnapshotId = await snapshots.Reconcile(Snapshot(2, original, other));
        (await importer.ImportListing(original)).Should().Be(id);
        (await importer.ImportListing(original)).Should().Be(id);
        (await DbContext.Set<EquityListing>().SingleAsync(row => row.Id == id))
            .Active.Should()
            .BeTrue();
    }

    [Fact]
    public async Task OlderOrConflictingSnapshotCannotReverseCurrentEligibility()
    {
        await using var services = Services();
        var factory = services.GetRequiredService<IServiceScopeFactory>();
        var snapshots = new EquityDirectorySnapshotManager(factory);
        var original = Listing();
        var currentId = await snapshots.Reconcile(Snapshot(2, original));
        var stale = () => snapshots.Reconcile(Snapshot(1, original));
        await stale.Should().ThrowAsync<InvalidDataException>();
        var conflicting = () => snapshots.Reconcile(Snapshot(2, Listing("PTEDP0AM0009")));
        await conflicting.Should().ThrowAsync<InvalidDataException>();
        (await DbContext.Set<EquityDirectorySnapshotState>().SingleAsync())
            .SourceRecordId.Should()
            .Be(currentId);
        (await DbContext.Set<EquityDirectorySourceRecord>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task EmptySnapshotCannotWithdrawEveryListing()
    {
        await using var services = Services();
        var snapshots = new EquityDirectorySnapshotManager(
            services.GetRequiredService<IServiceScopeFactory>()
        );
        var empty = () => snapshots.Reconcile(Snapshot(0));
        await empty.Should().ThrowAsync<InvalidDataException>();
        (await DbContext.Set<EquityDirectorySnapshotState>().CountAsync()).Should().Be(0);
    }
}
