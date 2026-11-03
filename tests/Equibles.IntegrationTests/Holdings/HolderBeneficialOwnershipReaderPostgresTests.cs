using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.BusinessLogic.Ownership;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// A filer's Schedule 13D and 13G stakes read from Postgres: the latest filing per exact listed
/// security with its prior, the status each gets, split restatement where the issuer trades, and
/// the sitemap's cheap activity proxy.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HolderBeneficialOwnershipReaderPostgresTests : ParadeDbMcpTestBase
{
    private static readonly DateOnly Today = new(2026, 11, 3);

    public HolderBeneficialOwnershipReaderPostgresTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Read_RanksEachSecuritysLatestFiling_HeldStakesFirstWithTheirStatus()
    {
        var holder = Holder("1661897", "Merus Global Investments, LLC");
        var active = Issuer("ACT", "Active Corp", "0000000001");
        var quiet = Issuer("OLD", "Quiet Holdings", "0000000002");
        var exited = Issuer("EXT", "Exit Corp", "0000000003");
        var low = Issuer("LOW", "Low Corp", "0000000004");
        var gone = Issuer(
            "GONE",
            "Gone Corp",
            "0000000005",
            active: false,
            delistedOn: new DateOnly(2026, 3, 26)
        );
        // A SPAC class that converted into the operating company's shares: the filed class is
        // inactive while the issuer trades under its new ticker.
        var renamed = Issuer("IMSR", "Terrestrial Energy Inc.", "0000000006", secondary: "HOND");
        foreach (
            var listing in renamed
                .Securities.SelectMany(security => security.Listings)
                .Where(listing => listing.Ticker == "HOND")
        )
            listing.Active = false;
        // A shell folded into its predecessor keeps no presentation line at all.
        var folded = Equibles.TestSupport.EquityIssuerSeed.Create(
            Name: "Folded Shell",
            Cik: "0000000007"
        );
        DbContext.Add(folded);
        // A SPAC whose class A converted at the merger: the filer's exit amendment lands on the
        // successor's common stock, so the retired class folds into the presentation line.
        var merged = Issuer("MRLN", "Merlin, Inc.", "0000000008", secondary: "BACQ");
        foreach (
            var listing in merged
                .Securities.SelectMany(security => security.Listings)
                .Where(listing => listing.Ticker == "BACQ")
        )
            listing.Active = false;

        // Filed under the presentation ticker before it became the presentation line: one
        // security with the later row filed under null, so the earlier one is its prior.
        Schedule(
            holder,
            active,
            new DateOnly(2026, 3, 2),
            1_200_000,
            7.1m,
            "g-act-0",
            listedTicker: "ACT"
        );
        Schedule(holder, active, new DateOnly(2026, 6, 8), 1_458_618, 8.6m, "g-act-1");
        // Effective after the event: the stake restates onto today's two-for-one basis.
        DbContext.Add(
            new StockSplit
            {
                EquityIssuerId = active.Id,
                EquityListingId = active.Presentation.EquityListingId,
                PriceSeriesTicker = "ACT",
                EffectiveDate = new DateOnly(2026, 9, 1),
                Numerator = 2,
                Denominator = 1,
            }
        );
        Schedule(holder, quiet, new DateOnly(2024, 2, 14), 700_000, 7m, "g-old-1");
        Schedule(holder, exited, new DateOnly(2025, 5, 27), 1_500_000, 6m, "g-ext-1");
        Schedule(holder, exited, new DateOnly(2026, 5, 15), 0, 0m, "g-ext-2", amendment: true);
        Schedule(holder, low, new DateOnly(2026, 2, 17), 400_000, 4.5m, "g-low-1", amendment: true);
        Schedule(holder, gone, new DateOnly(2026, 2, 26), 633_558, 9m, "g-gone-1");
        Schedule(
            holder,
            renamed,
            new DateOnly(2025, 5, 23),
            1_480_168,
            6.4m,
            "g-hond-1",
            listedTicker: "HOND"
        );
        Schedule(holder, folded, new DateOnly(2026, 6, 1), 250_000, 5.5m, "g-fold-1");
        Schedule(
            holder,
            merged,
            new DateOnly(2025, 12, 31),
            1_725_000,
            6.9m,
            "g-bacq-1",
            listedTicker: "BACQ"
        );
        Schedule(holder, merged, new DateOnly(2026, 3, 31), 0, 0m, "g-mrln-1", amendment: true);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var stakes = await Reader().Read(holder);

        stakes
            .Select(stake => (stake.Ticker, stake.Status))
            .Should()
            .Equal(
                ("ACT", Schedule13DGStakeStatus.Current),
                ("HOND", Schedule13DGStakeStatus.Current),
                ("OLD", Schedule13DGStakeStatus.Unconfirmed),
                (null, Schedule13DGStakeStatus.SecurityRetired),
                ("EXT", Schedule13DGStakeStatus.Exited),
                ("MRLN", Schedule13DGStakeStatus.Exited),
                ("GONE", Schedule13DGStakeStatus.SecurityRetired),
                ("LOW", Schedule13DGStakeStatus.BelowThreshold)
            );

        var mergedExit = stakes[5];
        mergedExit.AccessionNumber.Should().Be("g-mrln-1");
        mergedExit
            .ChangeShares.Should()
            .Be(-1_725_000, "the retired class's stake is the prior of the exit on the successor");
        mergedExit.PriorEventDate.Should().Be(new DateOnly(2025, 12, 31));

        var split = stakes[0];
        split.AccessionNumber.Should().Be("g-act-1");
        split.Shares.Should().Be(2_917_236);
        split.FiledShares.Should().Be(1_458_618);
        split.PercentOfClass.Should().Be(8.6m);
        split.CurrentTicker.Should().Be("ACT");
        split
            .ChangeShares.Should()
            .Be(2_917_236 - 2_400_000, "the row filed under the presentation ticker is its prior");
        split.PriorEventDate.Should().Be(new DateOnly(2026, 3, 2));

        var converted = stakes[1];
        converted.CurrentTicker.Should().Be("IMSR");
        converted.IssuerName.Should().Be("Terrestrial Energy Inc.");

        var shell = stakes[3];
        shell.CurrentTicker.Should().BeNull();
        shell
            .Shares.Should()
            .Be(250_000, "an issuer without a presentation line keeps filed counts");

        var exit = stakes[4];
        exit.IsAmendment.Should().BeTrue();
        exit.AccessionNumber.Should().Be("g-ext-2");
        exit.ChangeShares.Should().Be(-1_500_000);
        exit.PriorEventDate.Should().Be(new DateOnly(2025, 5, 27));

        stakes[6].DelistedOn.Should().Be(new DateOnly(2026, 3, 26));
    }

    [Fact]
    public async Task Read_NoSchedules_ReturnsNothing()
    {
        var holder = Holder("77", "Form 13F Only");
        var stock = Issuer("ONLY", "Only Corp", "0000000010");
        DbContext.Add(
            new InstitutionalHolding
            {
                EquityIssuerId = stock.Id,
                InstitutionalHolderId = holder.Id,
                ReportDate = new DateOnly(2026, 6, 30),
                FilingDate = new DateOnly(2026, 8, 14),
                Shares = 1_000,
                Value = 100_000,
                ShareType = ShareType.Shares,
                InvestmentDiscretion = InvestmentDiscretion.Sole,
                FilingType = FilingType.Form13F,
                AccessionNumber = "f-only-1",
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        (await Reader().Read(holder)).Should().BeEmpty();
    }

    [Fact]
    public async Task HolderActivity_ListsFilersWithAStakeAtOrAboveTheThreshold_DatedByThatFiling()
    {
        var reporter = Holder("1", "Reporter");
        var small = Holder("2", "Small Stake");
        var stock = Issuer("ACT", "Active Corp", "0000000001");
        Schedule(reporter, stock, new DateOnly(2026, 6, 8), 1_000, 8m, "g-rep-1");
        Schedule(reporter, stock, new DateOnly(2026, 8, 4), 0, 0m, "g-rep-2", amendment: true);
        Schedule(small, stock, new DateOnly(2026, 7, 1), 500, 4m, "g-small-1");
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var activity = await new InstitutionalHoldingRepository(verify)
            .GetSchedule13DGHolderActivity()
            .ToListAsync();

        var row = activity.Should().ContainSingle().Subject;
        row.InstitutionalHolderId.Should().Be(reporter.Id);
        row.LastFilingDate.Should().Be(new DateOnly(2026, 6, 18));
    }

    private HolderBeneficialOwnershipReader Reader()
    {
        var verify = Fixture.CreateDbContext();
        return new HolderBeneficialOwnershipReader(
            new InstitutionalHoldingRepository(verify),
            new EquityIssuerRepository(verify),
            new StockSplitRepository(verify),
            new FixedTimeProvider(Today)
        );
    }

    private InstitutionalHolder Holder(string cik, string name)
    {
        var holder = new InstitutionalHolder { Cik = cik, Name = name };
        DbContext.Add(holder);
        return holder;
    }

    private EquityIssuer Issuer(
        string ticker,
        string name,
        string cik,
        bool active = true,
        DateOnly? delistedOn = null,
        string secondary = null
    )
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: ticker,
            Name: name,
            Cik: cik,
            Active: active,
            DelistedOn: delistedOn,
            SecondaryTickers: secondary == null ? null : [secondary]
        );
        DbContext.Add(issuer);
        return issuer;
    }

    private void Schedule(
        InstitutionalHolder holder,
        EquityIssuer stock,
        DateOnly eventDate,
        long shares,
        decimal percent,
        string accession,
        bool amendment = false,
        string listedTicker = null
    ) =>
        DbContext.Add(
            new InstitutionalHolding
            {
                EquityIssuerId = stock.Id,
                InstitutionalHolderId = holder.Id,
                ReportDate = eventDate,
                FilingDate = eventDate.AddDays(10),
                Shares = shares,
                Value = 0,
                ShareType = ShareType.Shares,
                InvestmentDiscretion = InvestmentDiscretion.Sole,
                FilingType = FilingType.Schedule13G,
                PercentOfClass = percent,
                ListedTicker = listedTicker,
                AccessionNumber = accession,
                IsAmendment = amendment,
            }
        );

    private sealed class FixedTimeProvider(DateOnly day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }
}
