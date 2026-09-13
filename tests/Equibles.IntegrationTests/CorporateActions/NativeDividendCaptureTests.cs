using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.CorporateActions;

[Collection(ParadeDbCollection.Name)]
public class NativeDividendCaptureTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    private CashDividendCaptureManager Capture() =>
        new(new CashDividendRepository(DbContext), new EquityIssuerRepository(DbContext));

    private CorporateActionPriceReconciliationManager Reconcile() =>
        new(
            new StockSplitRepository(DbContext),
            new CashDividendRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new CorporateActionPriceReconciliationCursorRepository(DbContext)
        );

    private static CapturedDividend Payment(string currency = "USD", decimal amount = 1m) =>
        new()
        {
            ExDate = new(2025, 1, 2),
            AmountPerShare = amount,
            Currency = currency,
            Source = CashDividendSource.Yahoo,
        };

    private async Task<EquityIssuer> Seed()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "SAME",
            SecondaryTickers: ["CLASS"]
        );
        foreach (var listing in issuer.Securities.SelectMany(security => security.Listings))
            listing.TradingCurrency = "USD";
        DbContext.Add(issuer);
        await DbContext.SaveChangesAsync();
        return issuer;
    }

    [Fact]
    public async Task OriginalUnknownPayment_RemainsUnchangedBesideExactObservation()
    {
        var issuer = await Seed();
        var applied = new DateTime(2025, 1, 4, 0, 0, 0, DateTimeKind.Utc);
        var original = new CashDividend
        {
            EquityIssuerId = issuer.Id,
            ExDate = Payment().ExDate,
            AmountPerShare = 3m,
            Source = CashDividendSource.Manual,
            PriceAdjustmentAppliedAmountPerShare = 3m,
            PriceAdjustmentAppliedTime = applied,
        };
        DbContext.Add(original);
        await DbContext.SaveChangesAsync();
        var listing = issuer.Presentation.Listing;
        (await Capture().CaptureForListing(issuer.Id, listing.Id, listing.Ticker, [Payment()]))
            .Should()
            .Be(1);
        (await Capture().CaptureForListing(issuer.Id, listing.Id, listing.Ticker, [Payment()]))
            .Should()
            .Be(0);
        DbContext.ChangeTracker.Clear();
        var saved = await DbContext.Set<CashDividend>().SingleAsync(row => row.Id == original.Id);
        saved.EquityListingId.Should().BeNull();
        saved.Currency.Should().BeNull();
        saved.AmountPerShare.Should().Be(3m);
        saved.Source.Should().Be(CashDividendSource.Manual);
        saved.PriceAdjustmentAppliedTime.Should().Be(applied);
        saved.PriceAdjustmentAppliedAmountPerShare.Should().Be(3m);
        var history = await new CashDividendRepository(DbContext)
            .GetHistoryByListing(listing.Id)
            .ToListAsync();
        history.Should().ContainSingle().Which.AmountPerShare.Should().Be(1m);
        (await Reconcile().SelectPendingSeries(10, new(2025, 2, 1)))
            .Series.Should()
            .ContainSingle()
            .Which.Dividends.Should()
            .ContainSingle();
    }

    [Fact]
    public async Task SameSymbolAcrossVenues_QueuesAndStampsOnlyExactListing()
    {
        var issuer = await Seed();
        var local = issuer.Presentation.Listing;
        var foreign = new EquityListing
        {
            Ticker = "SAME",
            TradingCurrency = "EUR",
            MarketCountryCode = "PT",
            MarketIdentifierCode = "XLIS",
            Security = local.Security,
        };
        DbContext.Add(foreign);
        await DbContext.SaveChangesAsync();
        (await Capture().CaptureForListing(issuer.Id, local.Id, "SAME", [Payment()]))
            .Should()
            .Be(1);
        (await Capture().CaptureForListing(issuer.Id, foreign.Id, "SAME", [Payment("EUR", 2m)]))
            .Should()
            .Be(1);
        var manager = Reconcile();
        var first = (await manager.SelectPendingSeries(1, new(2025, 2, 1))).Series.Single();
        var second = (await manager.SelectPendingSeries(1, new(2025, 2, 1))).Series.Single();
        first.EquityListingId.Should().NotBe(second.EquityListingId);
        var selected = new[] { first, second }.Single(row => row.EquityListingId == foreign.Id);
        (
            await manager.StampApplied(
                selected,
                [Payment("USD", 2m)],
                new(2025, 2, 1),
                DateTime.UtcNow
            )
        )
            .Should()
            .Be(0);
        (
            await manager.StampApplied(
                selected,
                [Payment("EUR", 2m)],
                new(2025, 2, 1),
                DateTime.UtcNow
            )
        )
            .Should()
            .Be(1);
        (await new CashDividendRepository(DbContext).GetByListing(local.Id).SingleAsync())
            .PriceAdjustmentAppliedTime.Should()
            .BeNull();
        (await new CashDividendRepository(DbContext).GetByListing(foreign.Id).SingleAsync())
            .PriceAdjustmentAppliedTime.Should()
            .NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("EUR")]
    [InlineData("usd")]
    [InlineData("GBp")]
    public async Task UnknownOrConflictingCurrency_RefusesWholeDate(string invalidCurrency)
    {
        var issuer = await Seed();
        var listing = issuer.Presentation.Listing;
        (
            await Capture()
                .CaptureForListing(
                    issuer.Id,
                    listing.Id,
                    listing.Ticker,
                    [Payment(), Payment(invalidCurrency)]
                )
        )
            .Should()
            .Be(0);
        (await DbContext.Set<CashDividend>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task PrimaryReorder_DoesNotMoveSecondaryPaymentOrItsMarker()
    {
        var issuer = await Seed();
        var original = issuer.Presentation.Listing;
        var secondary = issuer
            .Securities.SelectMany(security => security.Listings)
            .Single(row => row.Ticker == "CLASS");
        (await Capture().CaptureForListing(issuer.Id, secondary.Id, "CLASS", [Payment()]))
            .Should()
            .Be(1);
        var manager = Reconcile();
        var selected = (await manager.SelectPendingSeries(10, new(2025, 2, 1))).Series.Single();
        issuer.Presentation.Listing = secondary;
        issuer.Presentation.EquityListingId = secondary.Id;
        await DbContext.SaveChangesAsync();
        (await manager.StampApplied(selected, DateTime.UtcNow)).Should().Be(1);
        (await new CashDividendRepository(DbContext).GetByListing(original.Id).AnyAsync())
            .Should()
            .BeFalse();
        secondary.Ticker = "RENAMED";
        await DbContext.SaveChangesAsync();
        (await Capture().CaptureForListing(issuer.Id, secondary.Id, "CLASS", [Payment(amount: 2m)]))
            .Should()
            .Be(0);
        (
            await Capture()
                .CaptureForListing(issuer.Id, secondary.Id, "RENAMED", [Payment(amount: 2m)])
        )
            .Should()
            .Be(1);
        (await manager.StampApplied(selected, DateTime.UtcNow)).Should().Be(0);
        var next = (await manager.SelectPendingSeries(10, new(2025, 2, 1))).Series.Single();
        next.EquityListingId.Should().Be(secondary.Id);
        next.ListedTicker.Should().Be("RENAMED");
    }

    [Fact]
    public async Task HistoricalCapture_RequiresSameRetirementAndExcludesLaterPayments()
    {
        var issuer = await Seed();
        var listing = issuer.Presentation.Listing;
        listing.Active = false;
        listing.DelistedOn = Payment().ExDate;
        await DbContext.SaveChangesAsync();
        var later = Payment();
        later.ExDate = later.ExDate.AddDays(1);
        (
            await Capture()
                .CaptureForHistoricalListing(
                    issuer.Id,
                    listing.Id,
                    listing.Ticker,
                    listing.DelistedOn.Value,
                    [Payment(), later]
                )
        )
            .Should()
            .Be(1);
        (
            await Capture()
                .CaptureForHistoricalListing(
                    issuer.Id,
                    listing.Id,
                    listing.Ticker,
                    listing.DelistedOn.Value.AddDays(1),
                    [Payment(amount: 2m)]
                )
        )
            .Should()
            .Be(0);
        var cutoff = listing.DelistedOn.Value;
        listing.Active = true;
        listing.DelistedOn = null;
        await DbContext.SaveChangesAsync();
        (
            await Capture()
                .CaptureForHistoricalListing(
                    issuer.Id,
                    listing.Id,
                    listing.Ticker,
                    cutoff,
                    [Payment(amount: 2m)]
                )
        )
            .Should()
            .Be(0);
        (await new CashDividendRepository(DbContext).GetByListing(listing.Id).SingleAsync())
            .AmountPerShare.Should()
            .Be(1m);
    }
}
