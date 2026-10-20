using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.BusinessLogic.Signals;
using Equibles.Holdings.BusinessLogic.Signals.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Caching.Memory;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Holder-overlap peers and shareholder targets are operating companies, one per issuer, on the
/// production context shape: funds are classified from the exchange-traded product reference feed
/// and Form N-PORT records, never names, and a filer's share classes of a peer count once.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HolderPeerCompaniesTests : IAsyncLifetime
{
    private static readonly DateOnly Quarter = new(2026, 3, 31);

    private readonly ParadeDbFixture _fixture;
    private EquityIssuer _nvidia;
    private EquityIssuer _alphabet;
    private EquityIssuer _bank;
    private EquityIssuer _spy;
    private EquityIssuer _closedEnd;
    private EquityIssuer _trust;
    private EquityIssuer _namedLikeAFund;
    private EquityIssuer _thin;

    public HolderPeerCompaniesTests(ParadeDbFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        await using var seed = _fixture.CreateDbContext();
        _nvidia = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "NVDA", Name: "NVIDIA Corp");
        _alphabet = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "GOOGL",
            Name: "Alphabet Inc.",
            SecondaryTickers: ["GOOG"]
        );
        // An operating bank whose exchange-traded note is on the reference feed.
        _bank = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "JPM",
            Name: "JPMorgan Chase & Co",
            ReferenceTickers: ["AMJ"]
        );
        _spy = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "SPY",
            Name: "SPDR S&P 500 ETF Trust",
            ReferenceTickers: ["SPY"]
        );
        _closedEnd = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "CEE",
            Name: "Central & Eastern Europe Corp"
        );
        _trust = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAXJ",
            Name: "Asia Trust",
            Cik: "1100663"
        );
        _namedLikeAFund = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "GFND",
            Name: "Global Index Fund ETF Holdings Inc"
        );
        _thin = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "THIN", Name: "Thin Corp");
        seed.AddRange(_nvidia, _alphabet, _bank, _spy, _closedEnd, _trust, _namedLikeAFund, _thin);
        seed.AddRange(
            Series(_closedEnd.Id, null, "S000000001"),
            Series(null, "0001100663", "S000000002")
        );

        var bothClasses = new InstitutionalHolder { Cik = "1000001", Name = "Both Classes Capital" };
        var noteOnly = new InstitutionalHolder { Cik = "1000002", Name = "Note Only Capital" };
        var alreadyHolds = new InstitutionalHolder { Cik = "1000003", Name = "Already Holds Capital" };
        seed.AddRange(bothClasses, noteOnly, alreadyHolds);
        seed.AddRange(
            Holding(_alphabet, null, bothClasses, 100_000_000),
            Holding(_alphabet, "GOOG", bothClasses, 50_000_000),
            Holding(_bank, "AMJ", noteOnly, 70_000_000),
            Holding(_alphabet, null, alreadyHolds, 90_000_000),
            Holding(_nvidia, null, alreadyHolds, 10_000_000),
            Metrics(bothClasses, 600_000_000),
            Metrics(noteOnly, 100_000_000),
            Metrics(alreadyHolds, 400_000_000),
            new HolderStockPresence
            {
                InstitutionalHolderId = alreadyHolds.Id,
                EquityIssuerId = _nvidia.Id,
                HeldMask = HolderTenureCalculator.Set(
                    0,
                    HolderTenureCalculator.QuarterIndex(Quarter).Value
                ),
            },
            new HolderIntelligenceQuarterState
            {
                ReportDate = Quarter,
                ComputedAt = new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc),
                CalculationVersion = HolderCostBasisCalculator.FirstVersion,
            },
            Signals(_nvidia.Id, null, filers: 10, conviction: 4),
            Signals(_alphabet.Id, "GOOG", filers: 12, conviction: 6),
            Signals(_thin.Id, null, filers: 3, conviction: 3),
            Overlap(_nvidia, 1, _alphabet),
            Overlap(_nvidia, 2, _bank),
            Overlap(_alphabet, 1, _nvidia)
        );
        await seed.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Load_ClassifiesFundsFromAuthoritativeRecordsOnly()
    {
        await using var context = _fixture.CreateDbContext();

        var funds = await FundSecurities.Load(
            context.Set<EquityIssuer>(),
            context.Set<FundSeries>(),
            null,
            CancellationToken.None
        );

        funds.IsFundIssuer(_spy.Id).Should().BeTrue("its presentation listing is an exchange-traded product");
        funds.IsFundIssuer(_closedEnd.Id).Should().BeTrue("it files Form N-PORT");
        funds.IsFundIssuer(_trust.Id).Should().BeTrue("its CIK registers an N-PORT series, however padded");
        funds.IsFundIssuer(_bank.Id).Should().BeFalse("a note it issues does not make the bank a fund");
        funds.IsFund(new HolderSecurityKey(_bank.Id, "AMJ")).Should().BeTrue();
        funds.IsFund(new HolderSecurityKey(_bank.Id, null)).Should().BeFalse();
        funds.IsFundIssuer(_namedLikeAFund.Id).Should().BeFalse("a name is never evidence");
        funds.IsFundIssuer(_nvidia.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Read_CountsAPeerOnceAcrossItsShareClassesAndNeverThroughAFundListing()
    {
        await using var context = _fixture.CreateDbContext();

        var targets = await TargetsReader(context).Read(_nvidia, "NVDA");

        targets.Unavailable.Should().BeNull();
        targets.Peers.Select(peer => peer.Ticker).Should().Equal("GOOGL", "JPM");
        var target = targets.Targets.Should().ContainSingle().Which;
        target.Name.Should().Be("Both Classes Capital");
        target.PeerValue.Should().Be(150_000_000);
        target.PeersHeld.Should().Be(1, "both Alphabet classes are one company");
        target.TopPeers.Should().Equal("GOOGL");
    }

    [Fact]
    public async Task Read_RefusesTargetsForAFund()
    {
        await using var context = _fixture.CreateDbContext();

        var targets = await TargetsReader(context).Read(_spy, "SPY");

        targets.Targets.Should().BeEmpty();
        targets.Unavailable.Should().Contain("operating companies");
    }

    [Fact]
    public async Task Read_GivesEveryShareClassItsCompanysPeers()
    {
        await using var context = _fixture.CreateDbContext();

        var view = await SignalsReader(context).Read(_alphabet, "GOOG");

        view.Unavailable.Should().BeNull();
        view.Peers.Select(peer => peer.Ticker).Should().Equal("NVDA");
    }

    [Fact]
    public async Task RankQuarter_LeavesStocksWithTooFewFilersUnranked()
    {
        await using var context = _fixture.CreateDbContext();
        var reader = SignalsReader(context);

        var conviction = await reader.RankQuarter(Quarter, StockSignalsSort.Conviction);
        var crowding = await reader.RankQuarter(Quarter, StockSignalsSort.Crowding);

        conviction.Select(row => row.Ticker).Should().Equal("NVDA");
        crowding.Select(row => row.Ticker).Should().Equal("NVDA");
    }

    private static HolderSignalsReader SignalsReader(EquiblesFinancialDbContext context) =>
        new(
            new HolderSignalsRepository(context),
            new InstitutionalHoldingRepository(context),
            new InstitutionalHolderRepository(context),
            new EquityIssuerRepository(context),
            new MemoryCache(new MemoryCacheOptions())
        );

    private static HolderTargetsReader TargetsReader(EquiblesFinancialDbContext context) =>
        new(
            new HolderSignalsRepository(context),
            new InstitutionalHoldingRepository(context),
            new InstitutionalHolderRepository(context),
            new SectorRepository(context),
            SignalsReader(context),
            new EquityIssuerRepository(context),
            new FundSeriesRepository(context),
            new MemoryCache(new MemoryCacheOptions())
        );

    private static FundSeries Series(Guid? issuerId, string registrantCik, string seriesId) =>
        new()
        {
            IdentityKey = issuerId != null ? $"cs:{issuerId}:{seriesId}" : $"rc:{registrantCik}:{seriesId}",
            Slug = $"fund-{seriesId.ToLowerInvariant()}",
            EquityIssuerId = issuerId,
            RegistrantCik = registrantCik,
            SeriesId = seriesId,
            LatestNportFilingId = Guid.NewGuid(),
            LatestReportPeriodDate = Quarter,
            LatestFilingDate = Quarter.AddDays(30),
        };

    private static InstitutionalHolding Holding(
        EquityIssuer issuer,
        string listedTicker,
        InstitutionalHolder holder,
        long value
    ) =>
        new()
        {
            EquityIssuerId = issuer.Id,
            ListedTicker = listedTicker,
            InstitutionalHolderId = holder.Id,
            ReportDate = Quarter,
            FilingDate = new DateOnly(2026, 5, 10),
            Shares = value / 100,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            FilingType = FilingType.Form13F,
            AccessionNumber = $"13f-{holder.Cik}-20260331",
        };

    private static HolderQuarterlyMetrics Metrics(InstitutionalHolder holder, long aum) =>
        new()
        {
            InstitutionalHolderId = holder.Id,
            ReportDate = Quarter,
            EquityAum = aum,
            StyleBand = HolderStyleBand.Concentrated,
            CalculationVersion = HolderCostBasisCalculator.FirstVersion,
        };

    private static StockQuarterlyHolderSignals Signals(
        Guid issuerId,
        string listedTicker,
        int filers,
        int conviction
    ) =>
        new()
        {
            EquityIssuerId = issuerId,
            ListedTicker = listedTicker,
            ReportDate = Quarter,
            Filers = filers,
            Shares = 1_000_000,
            Value = 100_000_000,
            ValuedShares = 1_000_000,
            ConvictionHolders = conviction,
            CrowdedShares = 500_000,
            SharesOutstanding = 10_000_000,
            SharesOutstandingAsOf = Quarter.AddDays(-40),
            SharesOutstandingBasis = SharesOutstandingBasis.Consolidated,
        };

    private static StockQuarterlyHolderOverlap Overlap(
        EquityIssuer issuer,
        int rank,
        EquityIssuer peer
    ) =>
        new()
        {
            EquityIssuerId = issuer.Id,
            ReportDate = Quarter,
            Rank = rank,
            PeerEquityIssuerId = peer.Id,
            SharedHolders = 5,
            Similarity = 0.5m,
        };
}
