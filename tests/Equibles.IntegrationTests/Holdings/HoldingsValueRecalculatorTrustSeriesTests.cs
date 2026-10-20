using Equibles.CommonStocks.Data.Models;
using Equibles.Core.Contracts;
using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// The production IVV position that stayed pending: iShares Trust lists AAXJ as its presentation
/// and IVV as one of hundreds of secondary funds, and IWF (another fund of the trust) split 4:1
/// after the 2025-12-31 report date. Shares, filed value and close are the stored production row.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsValueRecalculatorTrustSeriesTests : IAsyncLifetime
{
    private static readonly DateOnly ReportDate = new(2025, 12, 31);
    private const decimal IvvClose = 684.94m;

    private readonly ParadeDbFixture _fixture;
    private readonly IStockPriceProvider _priceProvider = Substitute.For<IStockPriceProvider>();
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public HoldingsValueRecalculatorTrustSeriesTests(ParadeDbFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private EquiblesFinancialDbContext CreateSharedContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    private HoldingsValueRecalculator CreateRecalculator()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(CreateSharedContext());
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return new HoldingsValueRecalculator(
            scopeFactory,
            _priceProvider,
            Substitute.For<ILogger<HoldingsValueRecalculator>>()
        );
    }

    private async Task<InstitutionalHolding> SeedTrust(bool withSeries)
    {
        var seedContext = CreateSharedContext();
        EquityIssuer trust = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "AAXJ",
            Name: "iShares Trust",
            Cik: "1100663",
            SecondaryTickers: ["IVV", "IWF"],
            ReferenceTickers: ["AAXJ", "IVV", "IWF"]
        );
        var iwf = trust
            .Securities.SelectMany(security => security.Listings)
            .Single(listing => listing.Ticker == "IWF");
        var holder = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "826000",
            Name = "Test Filer",
        };
        var holding = new InstitutionalHolding
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = trust.Id,
            InstitutionalHolderId = holder.Id,
            ReportDate = ReportDate,
            FilingDate = new DateOnly(2026, 2, 10),
            Shares = 1534,
            FiledValue = 1_050_679L,
            ValuePending = true,
            ValueRetryCount = 2,
            ValueLastRetryAt = DateTime.UtcNow.AddDays(-40),
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = "0000826000-26-000001",
            ListedTicker = "IVV",
        };

        seedContext.Set<EquityIssuer>().Add(trust);
        seedContext.Set<InstitutionalHolder>().Add(holder);
        seedContext.Set<InstitutionalHolding>().Add(holding);
        seedContext
            .Set<StockSplit>()
            .Add(
                new StockSplit
                {
                    Id = Guid.NewGuid(),
                    EquityIssuerId = trust.Id,
                    EquityListingId = iwf.Id,
                    PriceSeriesTicker = "IWF",
                    EffectiveDate = new DateOnly(2026, 4, 29),
                    Numerator = 4m,
                    Denominator = 1m,
                    PriceAdjustmentAppliedTime = new DateTime(
                        2026,
                        9,
                        22,
                        15,
                        7,
                        29,
                        DateTimeKind.Utc
                    ),
                }
            );
        if (withSeries)
        {
            seedContext
                .Set<FundSeries>()
                .AddRange(
                    Series(trust.Id, "S000022496", "AAXJ"),
                    Series(trust.Id, "S000004310", "IVV"),
                    Series(trust.Id, "S000004346", "IWF")
                );
        }
        await seedContext.SaveChangesAsync();

        _priceProvider
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, string, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new Dictionary<(Guid, string, DateOnly), decimal>
                {
                    [(trust.Id, "IVV", ReportDate)] = IvvClose,
                }
            );
        return holding;
    }

    private static FundSeries Series(Guid issuerId, string seriesId, string ticker) =>
        new()
        {
            IdentityKey = $"cs:{issuerId}:{seriesId}",
            Slug = seriesId.ToLowerInvariant(),
            EquityIssuerId = issuerId,
            SeriesId = seriesId,
            Ticker = ticker,
            ClassTickers = [ticker],
            LatestNportFilingId = Guid.NewGuid(),
        };

    private async Task<InstitutionalHolding> Reload(Guid id) =>
        await CreateSharedContext()
            .Set<InstitutionalHolding>()
            .AsNoTracking()
            .FirstAsync(h => h.Id == id);

    [Fact]
    public async Task Recalculate_SecondaryFundWhileAnotherFundSplit_DerivesItsValue()
    {
        var holding = await SeedTrust(withSeries: true);

        await CreateRecalculator().Recalculate(CancellationToken.None);

        var updated = await Reload(holding.Id);
        updated.ValuePending.Should().BeFalse();
        updated.Value.Should().Be((long)(1534 * IvvClose));
        updated.ValueSource.Should().Be(ValueSource.Derived);
    }

    [Fact]
    public async Task Recalculate_SecondaryListingWithoutRegisteredSeries_StaysPending()
    {
        var holding = await SeedTrust(withSeries: false);

        await CreateRecalculator().Recalculate(CancellationToken.None);

        var updated = await Reload(holding.Id);
        updated.ValuePending.Should().BeTrue();
        updated.Value.Should().Be(0L);
    }
}
