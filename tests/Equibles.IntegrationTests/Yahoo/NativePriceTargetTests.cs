using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.Integrations.Yahoo.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Worker;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.HostedService.Configuration;
using Equibles.Yahoo.HostedService.Services;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

[Collection(ParadeDbCollection.Name)]
public class NativePriceTargetTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    private readonly IYahooFinanceClient _client = Substitute.For<IYahooFinanceClient>();

    private YahooPriceImportService Service()
    {
        var issuerRepository = new EquityIssuerRepository(DbContext);
        var splitRepository = new StockSplitRepository(DbContext);
        var dividendRepository = new CashDividendRepository(DbContext);
        var scope = ServiceScopeSubstitute.Create(
            (typeof(StockSplitRepository), splitRepository),
            (
                typeof(CorporateActionPriceReconciliationManager),
                new CorporateActionPriceReconciliationManager(
                    splitRepository,
                    dividendRepository,
                    issuerRepository,
                    new CorporateActionPriceReconciliationCursorRepository(DbContext)
                )
            ),
            (typeof(EquityIssuerRepository), new EquityIssuerRepository(DbContext)),
            (
                typeof(EquityDailyStockPriceRepository),
                new EquityDailyStockPriceRepository(DbContext)
            )
        );
        return new YahooPriceImportService(
            scope,
            Substitute.For<ILogger<YahooPriceImportService>>(),
            _client,
            new TickerMapService(scope),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions()),
            Options.Create(new YahooPriceScraperOptions())
        );
    }

    private static Task<List<PriceSeriesTarget>> InvokeTargets(
        YahooPriceImportService service,
        string method,
        params object[] arguments
    ) =>
        (Task<List<PriceSeriesTarget>>)
            typeof(YahooPriceImportService)
                .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(service, arguments)!;

    [Fact]
    public async Task CurrentAndHistoricalQueues_RetainNativeListingAndEvidenceIds()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "CURRENT",
            SecondaryTickers: ["FORMER"]
        );
        var current = issuer.Presentation.Listing;
        var former = issuer
            .Securities.SelectMany(security => security.Listings)
            .Single(listing => listing.Ticker == "FORMER");
        former.Active = false;
        former.IsDirectoryListed = false;
        former.DelistedOn = new(2025, 1, 10);
        var evidence = new EquityListingRetirementEvidence
        {
            Issuer = issuer,
            ListedTicker = "FORMER",
            DelistedOn = former.DelistedOn.Value,
        };
        DbContext.Add(issuer);
        DbContext.Add(evidence);
        await DbContext.SaveChangesAsync();

        var service = Service();
        var live = await InvokeTargets(
            service,
            "BuildPriceSeriesTargets",
            new[] { issuer.Id },
            CancellationToken.None
        );
        live.Should().ContainSingle().Which.EquityListingId.Should().Be(current.Id);
        var historical = await InvokeTargets(
            service,
            "BuildHistoricalPriceTargets",
            CancellationToken.None
        );
        historical.Should().ContainSingle().Which.EquityListingId.Should().Be(former.Id);
        historical[0].HistoricalEvidenceId.Should().Be(evidence.Id);
        historical[0].HistoryEndDate.Should().Be(former.DelistedOn);

        former.PriceHistoryBackfilled = true;
        await DbContext.SaveChangesAsync();
        (await InvokeTargets(service, "BuildHistoricalPriceTargets", CancellationToken.None))
            .Should()
            .BeEmpty();
        (
            await InvokeTargets(
                service,
                "BuildPriceSeriesTargets",
                new[] { issuer.Id },
                CancellationToken.None
            )
        )
            .Should()
            .ContainSingle()
            .Which.EquityListingId.Should()
            .Be(current.Id);
    }

    [Fact]
    public async Task CrawlFreshness_UsesNativeIdDespiteRenamedSourceTicker()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "NEW",
            SecondaryTickers: ["OTHER"]
        );
        var listing = issuer.Presentation.Listing;
        DbContext.Add(issuer);
        DbContext.Add(
            new EquityDailyStockPrice
            {
                Listing = listing,
                SourceTicker = "OLD",
                Date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
                Open = 10,
                High = 10,
                Low = 10,
                Close = 10,
                AdjustedClose = 10,
                Volume = 10,
            }
        );
        await DbContext.SaveChangesAsync();
        var service = Service();
        var targets = await InvokeTargets(
            service,
            "BuildPriceSeriesTargets",
            new[] { issuer.Id },
            CancellationToken.None
        );
        var ordered = await InvokeTargets(
            service,
            "OrderByCrawlPriority",
            targets,
            CancellationToken.None
        );
        ordered.Select(target => target.Ticker).Should().Equal("NEW", "OTHER");
        (await DbContext.Set<EquityDailyStockPrice>().SingleAsync())
            .SourceTicker.Should()
            .Be("OLD");
    }

    [Fact]
    public async Task Import_QueuedTickerReassignedBeforeFetch_DoesNotRetargetTheRequest()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "CURRENT",
            SecondaryTickers: ["SIBLING"]
        );
        var original = issuer.Presentation.Listing;
        var sibling = issuer
            .Securities.SelectMany(security => security.Listings)
            .Single(listing => listing.Ticker == "SIBLING");
        DbContext.Add(issuer);
        await DbContext.SaveChangesAsync();
        var target = new PriceSeriesTarget("CURRENT", issuer.Id, original.Id, IsPrimary: true);
        original.Ticker = "FORMER";
        sibling.Ticker = "CURRENT";
        await DbContext.SaveChangesAsync();

        var task = (Task)
            typeof(YahooPriceImportService)
                .GetMethod("ImportTicker", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(
                    Service(),
                    [target, DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None]
                )!;
        await task;

        await _client
            .DidNotReceive()
            .GetChart(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
        (await DbContext.Set<EquityDailyStockPrice>().CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Repair_TickerReassignedDuringFetch_PreservesOriginalRow(
        bool servesReplacement
    )
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "CURRENT",
            SecondaryTickers: ["SIBLING"]
        );
        var original = issuer.Presentation.Listing;
        var sibling = issuer
            .Securities.SelectMany(security => security.Listings)
            .Single(listing => listing.Ticker == "SIBLING");
        var date = new DateOnly(2025, 1, 10);
        var price = new EquityDailyStockPrice
        {
            Listing = original,
            SourceTicker = "CURRENT",
            Date = date,
            Open = 10,
            High = 10,
            Low = 11,
            Close = 10,
            AdjustedClose = 9,
            Volume = 10,
        };
        DbContext.Add(issuer);
        DbContext.Add(price);
        await DbContext.SaveChangesAsync();
        _client
            .GetChart("CURRENT", date, date)
            .Returns(async _ =>
            {
                await using var directory = Fixture.CreateDbContext();
                await directory
                    .Set<EquityListing>()
                    .Where(row => row.Id == original.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.Ticker, "FORMER"));
                await directory
                    .Set<EquityListing>()
                    .Where(row => row.Id == sibling.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.Ticker, "CURRENT"));
                return servesReplacement ? Chart(date) : new YahooChartData();
            });

        await Service().RepairInvalidOhlc(CancellationToken.None);

        await using var read = Fixture.CreateDbContext();
        var retained = await read.Set<EquityDailyStockPrice>().SingleAsync();
        retained.Id.Should().Be(price.Id);
        retained.EquityListingId.Should().Be(original.Id);
        retained.SourceTicker.Should().Be("CURRENT");
        retained.Low.Should().Be(11);
        retained.AdjustedClose.Should().Be(9);
        retained.Volume.Should().Be(10);
    }

    [Fact]
    public async Task Repair_RenamedListing_UsesCurrentSymbolAndPreservesOriginalSource()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "CURRENT");
        var date = new DateOnly(2025, 1, 10);
        var price = new EquityDailyStockPrice
        {
            Listing = issuer.Presentation.Listing,
            SourceTicker = "FORMER",
            Date = date,
            Open = 10,
            High = 10,
            Low = 11,
            Close = 10,
            AdjustedClose = 9,
            Volume = 10,
        };
        DbContext.Add(issuer);
        DbContext.Add(price);
        await DbContext.SaveChangesAsync();
        _client.GetChart("CURRENT", date, date).Returns(Chart(date));

        await Service().RepairInvalidOhlc(CancellationToken.None);

        await using var read = Fixture.CreateDbContext();
        var repaired = await read.Set<EquityDailyStockPrice>().SingleAsync();
        repaired.Id.Should().Be(price.Id);
        repaired.EquityListingId.Should().Be(price.EquityListingId);
        repaired.SourceTicker.Should().Be("FORMER");
        repaired.Low.Should().Be(10);
        repaired.AdjustedClose.Should().Be(9);
        repaired.Volume.Should().Be(20);
        await _client.DidNotReceive().GetChart("FORMER", Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    public async Task SplitBoundaryAudit_UsesOnlyAttributedListingAcrossSourceRenames(
        bool originalDiscontinuous,
        bool siblingDiscontinuous,
        bool attributed
    )
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "CURRENT",
            SecondaryTickers: ["SIBLING"]
        );
        var original = issuer.Presentation.Listing;
        var sibling = issuer
            .Securities.SelectMany(security => security.Listings)
            .Single(listing => listing.Ticker == "SIBLING");
        var effective = new DateOnly(2025, 1, 10);
        var applied = DateTime.UtcNow;
        var split = new StockSplit
        {
            Issuer = issuer,
            Listing = attributed ? original : null,
            PriceSeriesTicker = "FORMER",
            EffectiveDate = effective,
            Numerator = 2,
            Denominator = 1,
            Source = StockSplitSource.Yahoo,
            PriceAdjustmentAppliedTime = applied,
        };
        original.TickerAliases.Add(
            new EquityListingTickerAlias
            {
                Listing = original,
                Ticker = "FORMER",
                EvidenceSource = "recorded-listing",
            }
        );
        DbContext.Add(issuer);
        await DbContext.SaveChangesAsync();
        DbContext.Add(split);
        AddBoundaryPrice(original, "FORMER", effective.AddDays(-1), 100);
        AddBoundaryPrice(original, "CURRENT", effective, originalDiscontinuous ? 50 : 100);
        if (siblingDiscontinuous)
        {
            AddBoundaryPrice(sibling, "FORMER", effective.AddDays(-1), 100);
            AddBoundaryPrice(sibling, "FORMER", effective, 50);
        }
        await DbContext.SaveChangesAsync();

        DbContext.ChangeTracker.Clear();
        var task = (Task)
            typeof(YahooPriceImportService)
                .GetMethod(
                    "RequeueStampedSplitBasisMismatches",
                    BindingFlags.Instance | BindingFlags.NonPublic
                )!
                .Invoke(
                    Service(),
                    [DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None]
                )!;
        await task;

        await using var read = Fixture.CreateDbContext();
        var stored = await read.Set<StockSplit>().SingleAsync();
        if (attributed && originalDiscontinuous)
            stored.PriceAdjustmentAppliedTime.Should().BeNull();
        else
            stored
                .PriceAdjustmentAppliedTime.Should()
                .BeCloseTo(applied, TimeSpan.FromMilliseconds(1));
        (await read.Set<EquityDailyStockPrice>().CountAsync())
            .Should()
            .Be(siblingDiscontinuous ? 4 : 2);
    }

    private void AddBoundaryPrice(
        EquityListing listing,
        string sourceTicker,
        DateOnly date,
        decimal close
    ) =>
        DbContext.Add(
            new EquityDailyStockPrice
            {
                Listing = listing,
                SourceTicker = sourceTicker,
                Date = date,
                Open = close,
                High = close,
                Low = close,
                Close = close,
                AdjustedClose = close,
                Volume = 10,
            }
        );

    private static YahooChartData Chart(DateOnly date) =>
        new()
        {
            Prices =
            [
                new HistoricalPrice
                {
                    Date = date,
                    Open = 10,
                    High = 10,
                    Low = 10,
                    Close = 10,
                    AdjustedClose = 10,
                    Volume = 20,
                },
            ],
        };
}
