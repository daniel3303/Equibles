using Equibles.CommonStocks.BusinessLogic.Directory;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Core.Configuration;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.HostedService.Configuration;
using Equibles.Yahoo.HostedService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using NSubstitute;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class SourceInstrumentSnapshotTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    private ServiceProvider Services() =>
        new ServiceCollection()
            .AddScoped<EquiblesFinancialDbContext>(_ => Fixture.CreateDbContext())
            .AddScoped<EquityIssuerRepository>()
            .AddScoped<EquityListingRepository>()
            .AddScoped<StockSplitRepository>()
            .AddScoped<CashDividendRepository>()
            .AddScoped<CorporateActionPriceReconciliationCursorRepository>()
            .AddScoped<CorporateActionPriceReconciliationManager>()
            .AddScoped<EquityDirectorySourceRecordRepository>()
            .BuildServiceProvider();

    private static EquityDirectoryListingInput Listing(
        string id = "share-a",
        string ticker = "00001",
        string currency = "HKD"
    ) =>
        new()
        {
            Source = "official-directory",
            SourceIssuerIdentifier = "issuer-a",
            IssuerName = "Source company",
            SourceSecurityIdentifier = "security-a",
            SourceListingIdentifier = id,
            Ticker = ticker,
            MarketCountryCode = "HK",
            MarketIdentifierCode = "XHKG",
            TradingCurrency = currency,
            QuoteUnitMultiplier = 1m,
            SecurityType = EquitySecurityKind.OrdinaryShare,
            ListedOn = new(2000, 1, 3),
            SourceUrl = "https://example.org/instruments/" + id,
            PayloadJson = JsonConvert.SerializeObject(
                new
                {
                    id,
                    ticker,
                    currency,
                }
            ),
        };

    private static EquityDirectorySnapshotInput Snapshot(
        int revision,
        params EquityDirectoryListingInput[] listings
    ) =>
        new()
        {
            Source = "official-directory",
            EvidenceSource = "official-directory-snapshot",
            SourceRecordKey = "https://example.org/instruments",
            PayloadJson = JsonConvert.SerializeObject(new { revision, listings }),
            ObservedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(revision),
            MarketCountryCode = "HK",
            MarketIdentifierCodes = ["XHKG"],
            Listings = listings
                .Select(row => new EquityDirectoryListingKey(
                    row.Isin,
                    row.MarketIdentifierCode,
                    row.Ticker,
                    row.TradingCurrency,
                    row.SourceSecurityIdentifier,
                    row.SourceListingIdentifier
                ))
                .ToList(),
        };

    [Fact]
    public async Task WithdrawalAndReturnPreserveSeparateCurrencyCountersAndRejectStaleImports()
    {
        await using var services = Services();
        var factory = services.GetRequiredService<IServiceScopeFactory>();
        var manager = new EquityDirectorySnapshotManager(factory);
        var importer = new EquityDirectoryIdentityImporter(factory);
        var first = Listing();
        var second = Listing("share-cny", "80001", "CNY");
        first.DirectorySnapshotId = second.DirectorySnapshotId = await manager.Reconcile(
            Snapshot(0, first, second)
        );
        var firstId = await importer.ImportListing(first);
        var secondId = await importer.ImportListing(second);
        second.DirectorySnapshotId = await manager.Reconcile(Snapshot(1, second));
        await importer.ImportListing(second);
        (await DbContext.Set<EquityListing>().AsNoTracking().SingleAsync(row => row.Id == firstId))
            .Active.Should()
            .BeFalse();
        (await DbContext.Set<EquityListing>().AsNoTracking().SingleAsync(row => row.Id == secondId))
            .Active.Should()
            .BeTrue();
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportListing(first));
        first.DirectorySnapshotId = await manager.Reconcile(Snapshot(2, first, second));
        (await importer.ImportListing(first)).Should().Be(firstId);
        (await DbContext.Set<EquityListing>().CountAsync()).Should().Be(2);
        (await DbContext.Set<EquitySecurity>().SingleAsync())
            .SecurityType.Should()
            .Be(EquitySecurityKind.OrdinaryShare);
        (await DbContext.Set<EquityListing>().SingleAsync(row => row.Id == firstId))
            .ListedOn.Should()
            .Be(new DateOnly(2000, 1, 3));
    }

    [Theory]
    [InlineData("currency")]
    [InlineData("security")]
    [InlineData("duplicate")]
    [InlineData("missing-binding")]
    public async Task InvalidSnapshotCannotChangeEligibilityOrEvidence(string conflict)
    {
        await using var services = Services();
        var factory = services.GetRequiredService<IServiceScopeFactory>();
        var manager = new EquityDirectorySnapshotManager(factory);
        var importer = new EquityDirectoryIdentityImporter(factory);
        var first = Listing();
        first.DirectorySnapshotId = await manager.Reconcile(Snapshot(0, first));
        var firstId = await importer.ImportListing(first);
        var changed = Listing();
        if (conflict == "currency")
            changed.TradingCurrency = "USD";
        if (conflict == "security")
            changed.SourceSecurityIdentifier = "unknown";
        if (conflict == "missing-binding")
            changed.SourceListingIdentifier = null;
        var next = conflict == "duplicate" ? Snapshot(1, changed, changed) : Snapshot(1, changed);
        var count = await DbContext.Set<EquityDirectorySourceRecord>().CountAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.Reconcile(next));
        (await DbContext.Set<EquityDirectorySourceRecord>().CountAsync()).Should().Be(count);
        (await DbContext.Set<EquityListing>().SingleAsync(row => row.Id == firstId))
            .Active.Should()
            .BeTrue();
    }

    [Fact]
    public async Task OtherDirectoryOwnershipSurvivesAndRenameKeepsTheOriginalListing()
    {
        await using var services = Services();
        var factory = services.GetRequiredService<IServiceScopeFactory>();
        var manager = new EquityDirectorySnapshotManager(factory);
        var importer = new EquityDirectoryIdentityImporter(factory);
        var foreign = Listing("foreign", "00002");
        foreign.Source = "other-directory";
        var foreignId = await importer.ImportListing(foreign);
        var first = Listing();
        first.DirectorySnapshotId = await manager.Reconcile(Snapshot(0, first));
        var firstId = await importer.ImportListing(first);
        var renamed = Listing(ticker: "00003");
        renamed.DirectorySnapshotId = await manager.Reconcile(Snapshot(1, renamed));
        (await DbContext.Set<EquityListing>().AsNoTracking().SingleAsync(row => row.Id == firstId))
            .Active.Should()
            .BeFalse();
        (await importer.ImportListing(renamed)).Should().Be(firstId);
        (
            await DbContext
                .Set<EquityListing>()
                .AsNoTracking()
                .SingleAsync(row => row.Id == foreignId)
        )
            .Active.Should()
            .BeTrue();
        (await DbContext.Set<EquityListing>().AsNoTracking().SingleAsync(row => row.Id == firstId))
            .Ticker.Should()
            .Be("00003");
    }

    [Fact]
    public async Task PriceCandidatesAndWriteGuardsRequireTheSameBoundInstrumentWithoutIsin()
    {
        await using var services = Services();
        var factory = services.GetRequiredService<IServiceScopeFactory>();
        var input = Listing();
        var id = await new EquityDirectoryIdentityImporter(factory).ImportListing(input);
        var service = new YahooPriceImportService(
            factory,
            NullLogger<YahooPriceImportService>(),
            null,
            null,
            null,
            Options.Create(new WorkerOptions()),
            Options.Create(new YahooPriceScraperOptions())
        );
        var targets = await service.BuildCatalogPriceTargets(CancellationToken.None);
        var target = targets.Should().ContainSingle().Subject;
        target.EquityListingId.Should().Be(id);
        target.ProviderSymbol.Should().Be("0001.HK");
        target.Isin.Should().BeNull();
        target.EquitySecurityId.Should().NotBeNull();
        var binding = YahooListingSource.SourceBinding(target);
        var query = DbContext.Set<EquityListing>().ForVerifiedSource(binding);
        (await query.CountAsync()).Should().Be(1);
        (
            await DbContext
                .Set<EquityListing>()
                .ForVerifiedSource(binding with { EquitySecurityId = Guid.NewGuid() })
                .CountAsync()
        )
            .Should()
            .Be(0);
        (
            await DbContext
                .Set<EquityListing>()
                .ForVerifiedSource(binding with { EquitySecurityId = null })
                .CountAsync()
        )
            .Should()
            .Be(0);
        // A verified-looking legacy row with no official instrument bindings remains ineligible.
        var unbound = new EquityIssuer
        {
            Name = "Unbound",
            Securities =
            [
                new EquitySecurity
                {
                    Listings =
                    [
                        new EquityListing
                        {
                            Ticker = "00002",
                            MarketCountryCode = "HK",
                            MarketIdentifierCode = "XHKG",
                            TradingCurrency = "HKD",
                            QuoteUnitMultiplier = 1m,
                            IdentityState = EquityIdentityState.Verified,
                            IsDirectoryListed = true,
                            IdentitySourceUrl = "https://example.org/unbound",
                        },
                    ],
                },
            ],
        };
        DbContext.Add(unbound);
        await DbContext.SaveChangesAsync();
        (await service.BuildCatalogPriceTargets(CancellationToken.None)).Should().ContainSingle();
    }

    [Theory]
    [InlineData("currency")]
    [InlineData("security")]
    [InlineData("venue")]
    public async Task ConflictingReturnOfWithdrawnBindingCannotAdvanceTheSnapshot(string conflict)
    {
        await using var services = Services();
        var factory = services.GetRequiredService<IServiceScopeFactory>();
        var manager = new EquityDirectorySnapshotManager(factory);
        var importer = new EquityDirectoryIdentityImporter(factory);
        var original = Listing();
        original.DirectorySnapshotId = await manager.Reconcile(Snapshot(0, original));
        await importer.ImportListing(original);
        var other = Listing("other", "00002");
        other.DirectorySnapshotId = await manager.Reconcile(Snapshot(1, other));
        var otherId = await importer.ImportListing(other);
        var changed = Listing();
        if (conflict == "currency")
            changed.TradingCurrency = "USD";
        if (conflict == "security")
            changed.SourceSecurityIdentifier = "other-security";
        if (conflict == "venue")
            changed.MarketIdentifierCode = "XSHE";
        var next = Snapshot(2, changed);
        next.MarketIdentifierCodes = ["XHKG", "XSHE"];
        var evidence = await DbContext.Set<EquityDirectorySourceRecord>().CountAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.Reconcile(next));
        (await DbContext.Set<EquityDirectorySourceRecord>().CountAsync()).Should().Be(evidence);
        (await DbContext.Set<EquityListing>().SingleAsync(row => row.Id == otherId))
            .Active.Should()
            .BeTrue();
        (await DbContext.Set<EquityDirectorySnapshotState>().SingleAsync())
            .SourceRecordId.Should()
            .Be(other.DirectorySnapshotId.Value);
    }

    [Fact]
    public async Task SharedListingRemainsActiveUntilBothDirectoryClaimsAreWithdrawn()
    {
        await using var services = Services();
        var factory = services.GetRequiredService<IServiceScopeFactory>();
        var manager = new EquityDirectorySnapshotManager(factory);
        var importer = new EquityDirectoryIdentityImporter(factory);
        var first = Listing();
        first.Isin = "KYG217651051";
        first.DirectorySnapshotId = await manager.Reconcile(Snapshot(0, first));
        var id = await importer.ImportListing(first);
        var second = Listing();
        second.Isin = first.Isin;
        second.Source = "other-directory";
        (await importer.ImportListing(second)).Should().Be(id);
        var remainder = Listing("remainder", "00002");
        await manager.Reconcile(Snapshot(1, remainder));
        (await DbContext.Set<EquityListing>().AsNoTracking().SingleAsync(row => row.Id == id))
            .Active.Should()
            .BeTrue();
        var otherSnapshot = Snapshot(2, remainder);
        otherSnapshot.Source = second.Source;
        await manager.Reconcile(otherSnapshot);
        (await DbContext.Set<EquityListing>().AsNoTracking().SingleAsync(row => row.Id == id))
            .Active.Should()
            .BeFalse();
        (
            await DbContext
                .Set<EquityListingSourceIdentifier>()
                .Where(row => row.EquityListingId == id && row.IsDirectoryListed)
                .CountAsync()
        )
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task NoncanonicalHongKongSpellingCannotClaimTheCanonicalProviderSymbol()
    {
        await using var services = Services();
        var factory = services.GetRequiredService<IServiceScopeFactory>();
        var importer = new EquityDirectoryIdentityImporter(factory);
        var id = await importer.ImportListing(Listing());
        var alias = Listing("other", "0001");
        alias.SourceSecurityIdentifier = "another-security";
        await importer.ImportListing(alias);
        var service = new YahooPriceImportService(
            factory,
            NullLogger<YahooPriceImportService>(),
            null,
            null,
            null,
            Options.Create(new WorkerOptions()),
            Options.Create(new YahooPriceScraperOptions())
        );
        var targets = await service.BuildCatalogPriceTargets(CancellationToken.None);
        targets.Should().ContainSingle().Which.EquityListingId.Should().Be(id);
        var invalid = new PriceSeriesTarget(
            alias.Ticker,
            Guid.NewGuid(),
            Guid.NewGuid(),
            false,
            MarketCountryCode: "HK",
            MarketIdentifierCode: "XHKG",
            TradingCurrency: "HKD",
            QuoteUnitMultiplier: 1m,
            EquitySecurityId: Guid.NewGuid()
        );
        var binding = () => YahooListingSource.SourceBinding(invalid);
        binding.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task OneMarketSnapshotPreservesTheSameSourcesClaimsInAnotherMarket()
    {
        await using var services = Services();
        var factory = services.GetRequiredService<IServiceScopeFactory>();
        var importer = new EquityDirectoryIdentityImporter(factory);
        await importer.ImportListing(Listing());
        var foreign = Listing("foreign-market", "ALPHA", "EUR");
        foreign.MarketIdentifierCode = "XLIS";
        foreign.MarketCountryCode = "PT";
        var foreignId = await importer.ImportListing(foreign);
        await new EquityDirectorySnapshotManager(factory).Reconcile(
            Snapshot(0, Listing("other", "00002"))
        );
        (
            await DbContext
                .Set<EquityListingSourceIdentifier>()
                .SingleAsync(row => row.EquityListingId == foreignId)
        )
            .IsDirectoryListed.Should()
            .BeTrue();
        (
            await DbContext
                .Set<EquityListing>()
                .WithDirectoryInstrumentIdentity()
                .CountAsync(row => row.Id == foreignId)
        )
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task NewCounterWithConflictingBoundSecurityIsinCannotCommitSnapshot()
    {
        await using var services = Services();
        var factory = services.GetRequiredService<IServiceScopeFactory>();
        var manager = new EquityDirectorySnapshotManager(factory);
        var importer = new EquityDirectoryIdentityImporter(factory);
        var first = Listing();
        first.Isin = "KYG217651051";
        first.DirectorySnapshotId = await manager.Reconcile(Snapshot(0, first));
        var id = await importer.ImportListing(first);
        var counter = Listing("new-counter", "80001", "CNY");
        counter.Isin = "HK0002007356";
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            manager.Reconcile(Snapshot(1, counter))
        );
        (await DbContext.Set<EquityDirectorySnapshotState>().SingleAsync())
            .SourceRecordId.Should()
            .Be(first.DirectorySnapshotId.Value);
        (await DbContext.Set<EquityListing>().SingleAsync(row => row.Id == id))
            .Active.Should()
            .BeTrue();
        (await DbContext.Set<EquityListingSourceIdentifier>().SingleAsync())
            .IsDirectoryListed.Should()
            .BeTrue();
    }

    [Fact]
    public async Task PendingSplitCannotAdmitAnUnboundNonIsinListingOrWriteItsPrices()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "00001");
        var listing = issuer.Presentation.Listing;
        listing.MarketCountryCode = "HK";
        listing.MarketIdentifierCode = "XHKG";
        listing.TradingCurrency = "HKD";
        listing.QuoteUnitMultiplier = 1m;
        listing.IdentityState = EquityIdentityState.Verified;
        listing.IdentitySourceUrl = "https://example.org/unbound";
        listing.Security.Isin = null;
        DbContext.Add(issuer);
        var day = new DateOnly(2026, 9, 1);
        var split = new StockSplit
        {
            EquityIssuerId = issuer.Id,
            EquityListingId = listing.Id,
            PriceSeriesTicker = listing.Ticker,
            Source = StockSplitSource.Yahoo,
            EffectiveDate = day,
            Numerator = 2m,
            Denominator = 1m,
        };
        DbContext.Add(split);
        await DbContext.SaveChangesAsync();
        await using var services = Services();
        var factory = services.GetRequiredService<IServiceScopeFactory>();
        using var scope = services.CreateScope();
        var pending = await scope
            .ServiceProvider.GetRequiredService<CorporateActionPriceReconciliationManager>()
            .SelectPendingSeries(10, day.AddDays(2));
        pending.Series.Should().ContainSingle();
        var client = Substitute.For<IYahooFinanceClient>();
        var service = new YahooPriceImportService(
            factory,
            NullLogger<YahooPriceImportService>(),
            client,
            null,
            null,
            Options.Create(new WorkerOptions()),
            Options.Create(new YahooPriceScraperOptions())
        );
        await service.ReconcileStock(
            pending.Series[0],
            day.AddYears(-1),
            day.AddDays(2),
            CancellationToken.None
        );
        await client.DidNotReceiveWithAnyArgs().GetChart(default, default, default);
        (await DbContext.Set<StockSplit>().AsNoTracking().SingleAsync())
            .PriceAdjustmentAppliedTime.Should()
            .BeNull();
        var binding = YahooListingSource.SourceBinding(
            new PriceSeriesTarget(
                listing.Ticker,
                issuer.Id,
                listing.Id,
                true,
                MarketCountryCode: "HK",
                MarketIdentifierCode: "XHKG",
                TradingCurrency: "HKD",
                QuoteUnitMultiplier: 1m,
                EquitySecurityId: listing.EquitySecurityId
            )
        );
        (await DbContext.Set<EquityListing>().ForVerifiedSource(binding).CountAsync())
            .Should()
            .Be(0);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("date")]
    public async Task ConflictingAuthoritativeMetadataDoesNotPartiallyUpdateAnIdentity(
        string conflict
    )
    {
        await using var services = Services();
        var importer = new EquityDirectoryIdentityImporter(
            services.GetRequiredService<IServiceScopeFactory>()
        );
        var id = await importer.ImportListing(Listing());
        var changed = Listing();
        if (conflict == "type")
            changed.SecurityType = EquitySecurityKind.DepositaryReceipt;
        if (conflict == "date")
            changed.ListedOn = new(2001, 1, 1);
        changed.PayloadJson = "{\"changed\":true}";
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportListing(changed));
        (await DbContext.Set<EquityDirectorySourceRecord>().CountAsync()).Should().Be(1);
        var listing = await DbContext
            .Set<EquityListing>()
            .Include(row => row.Security)
            .SingleAsync(row => row.Id == id);
        listing.Security.SecurityType.Should().Be(EquitySecurityKind.OrdinaryShare);
        listing.ListedOn.Should().Be(new DateOnly(2000, 1, 3));
    }
}
