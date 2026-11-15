using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Pins the listed-CUSIP transfer on a sector fund's shape: the fund's symbol was first carried by
/// its sponsor, the fails archive recorded the fund's CUSIP there, and the directory later listed
/// the symbol under the fund's own trust. The claim and every position filed through it must
/// follow the listing, and nothing may move when the stored facts leave the owner in doubt.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ListedCusipTransferServiceTests : IAsyncLifetime
{
    private const string FundTicker = "XLF";
    private const string FundCusip = "81369Y605";
    private const string EarlierFundCusip = "81369Y506";
    private const string SponsorCusip = "857477103";
    private static readonly DateOnly Quarter = new(2026, 3, 31);

    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private Guid _sponsorId;
    private Guid _trustId;
    private Guid _holderId;
    private Guid _claimId;

    public ListedCusipTransferServiceTests(ParadeDbFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var context in _contexts)
            context.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>The ways the stored facts can leave the fund's owner in doubt.</summary>
    public enum Doubt
    {
        None,
        EarlierListingStillLive,
        EarlierListingWasReferenceListed,
        EarlierListingDelisted,
        EarlierIssuerNeverListedTheSymbol,
        SecondIssuerListsTheSymbol,
        LiveListingIsNotAReferenceListing,
        SymbolIsTheListersPresentation,
        ListerHoldsItsOwnCusip,
        ListerHoldsItsOwnClaim,
        NoFundSeries,
        FundSeriesOfAnotherIssuer,
        TwoFundSeriesCarryTheSymbol,
    }

    [Fact]
    public async Task Transfer_MovesTheClaimAndItsPositionsToTheIssuerListingTheFund()
    {
        await Seed(Doubt.None);
        var fund = await SeedHolding(_sponsorId, FundCusip, FundTicker, withManagerLeg: true);
        var stake = await SeedHolding(
            _sponsorId,
            FundCusip,
            FundTicker,
            filingType: FilingType.Schedule13G
        );
        var sponsorShares = await SeedHolding(_sponsorId, SponsorCusip, null);

        var result = await CreateService().Transfer(CancellationToken.None);

        result.Should().Be(new ListedCusipTransferService.Result(1, 1, 2, 0));
        (await ClaimOwner(_claimId)).Should().Be(_trustId);
        var moved = await Reload(fund);
        moved.EquityIssuerId.Should().Be(_trustId);
        moved.ListedTicker.Should().Be(FundTicker);
        moved.ManagerEntries.Should().ContainSingle().Which.ManagerName.Should().Be("Adviser");
        (await Reload(stake)).EquityIssuerId.Should().Be(_trustId);
        // The sponsor's own shares are its own security and stay.
        (await Reload(sponsorShares))
            .EquityIssuerId.Should()
            .Be(_sponsorId);
        await using var read = FreshContext();
        (await read.Set<AumQuarterlySnapshot>().SingleAsync(s => s.ReportDate == Quarter))
            .DirtyAt.Should()
            .NotBeNull();
    }

    [Fact]
    public async Task Transfer_MovesEveryCusipTheEarlierIssuerHoldsForTheSymbol()
    {
        await Seed(Doubt.None);
        // A fund keeps its symbol when a reorganisation gives it a new CUSIP.
        var earlierClaim = await SeedClaim(_sponsorId, FundTicker, EarlierFundCusip);
        var current = await SeedHolding(_sponsorId, FundCusip, FundTicker);
        var earlier = await SeedHolding(
            _sponsorId,
            EarlierFundCusip,
            FundTicker,
            reportDate: Quarter.AddMonths(-3)
        );

        var result = await CreateService().Transfer(CancellationToken.None);

        result.Should().Be(new ListedCusipTransferService.Result(1, 2, 2, 0));
        (await ClaimOwner(_claimId)).Should().Be(_trustId);
        (await ClaimOwner(earlierClaim)).Should().Be(_trustId);
        (await Reload(current)).EquityIssuerId.Should().Be(_trustId);
        (await Reload(earlier)).EquityIssuerId.Should().Be(_trustId);
    }

    [Fact]
    public async Task Transfer_PositionTheListingIssuerAlreadyHolds_StaysWithTheEarlierIssuer()
    {
        await Seed(Doubt.None);
        var blocked = await SeedHolding(_sponsorId, FundCusip, FundTicker, shares: 5);
        var held = await SeedHolding(_trustId, null, FundTicker, shares: 9);
        var free = await SeedHolding(
            _sponsorId,
            FundCusip,
            FundTicker,
            reportDate: Quarter.AddMonths(-3)
        );

        var result = await CreateService().Transfer(CancellationToken.None);

        // Merging two stored rows would lose one of them; the count says one was left behind.
        result.Should().Be(new ListedCusipTransferService.Result(1, 1, 1, 1));
        (await Reload(blocked)).EquityIssuerId.Should().Be(_sponsorId);
        (await Reload(held)).Shares.Should().Be(9);
        (await Reload(free)).EquityIssuerId.Should().Be(_trustId);
    }

    [Fact]
    public async Task Transfer_SecondPass_MovesNothing()
    {
        await Seed(Doubt.None);
        var fund = await SeedHolding(_sponsorId, FundCusip, FundTicker);
        await CreateService().Transfer(CancellationToken.None);

        var second = await CreateService().Transfer(CancellationToken.None);

        second.Should().Be(new ListedCusipTransferService.Result(0, 0, 0, 0));
        (await Reload(fund)).EquityIssuerId.Should().Be(_trustId);
    }

    [Theory]
    [InlineData(Doubt.EarlierListingStillLive)]
    [InlineData(Doubt.EarlierListingWasReferenceListed)]
    [InlineData(Doubt.EarlierListingDelisted)]
    [InlineData(Doubt.EarlierIssuerNeverListedTheSymbol)]
    [InlineData(Doubt.SecondIssuerListsTheSymbol)]
    [InlineData(Doubt.LiveListingIsNotAReferenceListing)]
    [InlineData(Doubt.SymbolIsTheListersPresentation)]
    [InlineData(Doubt.ListerHoldsItsOwnCusip)]
    [InlineData(Doubt.ListerHoldsItsOwnClaim)]
    [InlineData(Doubt.NoFundSeries)]
    [InlineData(Doubt.FundSeriesOfAnotherIssuer)]
    [InlineData(Doubt.TwoFundSeriesCarryTheSymbol)]
    public async Task Transfer_OwnerInDoubt_LeavesTheClaimAndItsPositions(Doubt doubt)
    {
        await Seed(doubt);
        var fund = await SeedHolding(_sponsorId, FundCusip, FundTicker);

        var result = await CreateService().Transfer(CancellationToken.None);

        result.Should().Be(new ListedCusipTransferService.Result(0, 0, 0, 0));
        (await ClaimOwner(_claimId)).Should().Be(_sponsorId);
        (await Reload(fund)).EquityIssuerId.Should().Be(_sponsorId);
        await using var read = FreshContext();
        (await read.Set<AumQuarterlySnapshot>().SingleAsync(s => s.ReportDate == Quarter))
            .DirtyAt.Should()
            .BeNull();
    }

    private async Task Seed(Doubt doubt)
    {
        var sponsor = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "STT",
            Name: "Sponsor Corp",
            Cik: "93751",
            Cusip: SponsorCusip
        );
        if (doubt != Doubt.EarlierIssuerNeverListedTheSymbol)
        {
            // The directory's leftover: retired, never the fund's reference listing, no end date.
            var leftover = UsEquityDirectory.GetOrAddListing(sponsor, FundTicker);
            leftover.Active = doubt == Doubt.EarlierListingStillLive;
            leftover.WasReferenceListed = doubt == Doubt.EarlierListingWasReferenceListed;
            leftover.DelistedOn =
                doubt == Doubt.EarlierListingDelisted ? new DateOnly(2025, 6, 30) : null;
        }

        var listsAsPresentation = doubt == Doubt.SymbolIsTheListersPresentation;
        var trust = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: listsAsPresentation ? FundTicker : "XLBI",
            Name: "Sector Trust",
            Cik: "1064641",
            ReferenceTickers: doubt == Doubt.LiveListingIsNotAReferenceListing ? [] : [FundTicker]
        );
        var live = UsEquityDirectory.GetOrAddListing(trust, FundTicker);
        if (doubt == Doubt.ListerHoldsItsOwnCusip)
            live.Security.Cusip = EarlierFundCusip;

        var other = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "OTHR",
            Name: "Other Trust",
            Cik: "1100663",
            ReferenceTickers: doubt == Doubt.SecondIssuerListsTheSymbol ? [FundTicker] : []
        );
        if (doubt == Doubt.SecondIssuerListsTheSymbol)
            // Two live listings of one symbol sit on different venues.
            UsEquityDirectory.GetOrAddListing(other, FundTicker).MarketIdentifierCode = "ARCX";

        _sponsorId = sponsor.Id;
        _trustId = trust.Id;
        var holder = new InstitutionalHolder { Cik = "0001000001", Name = "Sample Capital" };
        _holderId = holder.Id;

        await using var seed = FreshContext();
        seed.Set<EquityIssuer>().AddRange(sponsor, trust, other);
        seed.Set<InstitutionalHolder>().Add(holder);
        seed.Set<AumQuarterlySnapshot>().Add(new AumQuarterlySnapshot { ReportDate = Quarter });
        if (doubt != Doubt.NoFundSeries)
            seed.Set<FundSeries>()
                .Add(
                    Series(
                        doubt == Doubt.FundSeriesOfAnotherIssuer ? other.Id : trust.Id,
                        "S000006411"
                    )
                );
        if (doubt == Doubt.TwoFundSeriesCarryTheSymbol)
            seed.Set<FundSeries>().Add(Series(other.Id, "S000009999"));
        await seed.SaveChangesAsync();

        _claimId = await SeedClaim(_sponsorId, FundTicker, FundCusip);
        if (doubt == Doubt.ListerHoldsItsOwnClaim)
            await SeedClaim(_trustId, FundTicker, EarlierFundCusip);
    }

    private static FundSeries Series(Guid issuerId, string seriesId) =>
        new()
        {
            IdentityKey = $"cs:{issuerId}:{seriesId}",
            Slug = $"sector-fund-{seriesId.ToLowerInvariant()}",
            EquityIssuerId = issuerId,
            SeriesId = seriesId,
            SeriesName = "Sector Fund",
            ClassTickers = [FundTicker],
            LatestNportFilingId = Guid.NewGuid(),
            LatestReportPeriodDate = Quarter,
            LatestFilingDate = Quarter.AddDays(40),
        };

    private async Task<Guid> SeedClaim(Guid issuerId, string ticker, string cusip)
    {
        var claim = new EquityListingCusipEvidence
        {
            EquityIssuerId = issuerId,
            ListedTicker = ticker,
            Cusip = cusip,
        };
        await using var seed = FreshContext();
        seed.Set<EquityListingCusipEvidence>().Add(claim);
        await seed.SaveChangesAsync();
        return claim.Id;
    }

    private async Task<Guid> SeedHolding(
        Guid issuerId,
        string cusip,
        string listedTicker,
        long shares = 3,
        DateOnly? reportDate = null,
        FilingType filingType = FilingType.Form13F,
        bool withManagerLeg = false
    )
    {
        var date = reportDate ?? Quarter;
        var holding = new InstitutionalHolding
        {
            InstitutionalHolderId = _holderId,
            EquityIssuerId = issuerId,
            FilingDate = date.AddDays(40),
            ReportDate = date,
            Shares = shares,
            Value = shares * 40,
            ShareType = ShareType.Shares,
            FilingType = filingType,
            Cusip = cusip,
            ListedTicker = listedTicker,
            AccessionNumber = "0001000001-26-000001",
        };
        if (withManagerLeg)
            holding.ManagerEntries.Add(
                new HoldingManagerEntry
                {
                    ManagerNumber = 1,
                    ManagerName = "Adviser",
                    Shares = shares,
                    Value = shares * 40,
                }
            );
        await using var seed = FreshContext();
        seed.Set<InstitutionalHolding>().Add(holding);
        await seed.SaveChangesAsync();
        return holding.Id;
    }

    private ListedCusipTransferService CreateService()
    {
        var context = FreshContext();
        return new ListedCusipTransferService(
            ServiceScopeSubstitute.Create(
                (typeof(EquiblesFinancialDbContext), context),
                (typeof(EquityIssuerRepository), new EquityIssuerRepository(context))
            ),
            Substitute.For<ILogger<ListedCusipTransferService>>()
        );
    }

    private EquiblesFinancialDbContext FreshContext()
    {
        var context = _fixture.CreateDbContext();
        _contexts.Add(context);
        return context;
    }

    private async Task<Guid> ClaimOwner(Guid claimId)
    {
        await using var read = FreshContext();
        return await read.Set<EquityListingCusipEvidence>()
            .Where(claim => claim.Id == claimId)
            .Select(claim => claim.EquityIssuerId)
            .SingleAsync();
    }

    private async Task<InstitutionalHolding> Reload(Guid id)
    {
        await using var read = FreshContext();
        return await read.Set<InstitutionalHolding>().AsNoTracking().SingleAsync(h => h.Id == id);
    }
}
