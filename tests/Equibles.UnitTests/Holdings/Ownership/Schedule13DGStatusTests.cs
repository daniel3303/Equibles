using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.BusinessLogic.Ownership;

namespace Equibles.UnitTests.Holdings.Ownership;

public class Schedule13DGStatusTests
{
    private static readonly DateOnly Today = new(2026, 11, 3);

    [Fact]
    public void Of_RecentStakeAtOrAboveThreshold_IsCurrent()
    {
        Schedule13DGStatus
            .Of(1_000, 6.2m, new DateOnly(2026, 2, 14), Today, issuerListed: true)
            .Should()
            .Be(Schedule13DGStakeStatus.Current);
    }

    [Fact]
    public void Of_NoFilingInTwoYears_IsUnconfirmedNotExited()
    {
        var twoYearsAgo = Today.AddMonths(-Schedule13DGStatus.UnconfirmedMonths);

        Schedule13DGStatus
            .Of(1_000, 6.2m, twoYearsAgo.AddDays(-1), Today, issuerListed: true)
            .Should()
            .Be(Schedule13DGStakeStatus.Unconfirmed);
        Schedule13DGStatus
            .Of(1_000, 6.2m, twoYearsAgo, Today, issuerListed: true)
            .Should()
            .Be(Schedule13DGStakeStatus.Current, "the threshold day itself is still confirmed");
    }

    [Fact]
    public void Of_LatestFilingUnderFivePercent_IsBelowThresholdWhateverItsAge()
    {
        Schedule13DGStatus
            .Of(900, 4.9m, new DateOnly(2020, 2, 14), Today, issuerListed: true)
            .Should()
            .Be(Schedule13DGStakeStatus.BelowThreshold);
        Schedule13DGStatus
            .Of(900, null, new DateOnly(2020, 2, 14), Today, issuerListed: true)
            .Should()
            .Be(
                Schedule13DGStakeStatus.Unconfirmed,
                "a filing with no percent is never below the threshold"
            );
    }

    [Fact]
    public void Of_ExitedBeatsRetired_AndRetiredBeatsBelowThreshold()
    {
        Schedule13DGStatus
            .Of(0, 0m, new DateOnly(2026, 5, 15), Today, issuerListed: false)
            .Should()
            .Be(Schedule13DGStakeStatus.Exited);
        Schedule13DGStatus
            .Of(900, 4.9m, new DateOnly(2026, 5, 15), Today, issuerListed: false)
            .Should()
            .Be(Schedule13DGStakeStatus.SecurityRetired);
    }

    [Fact]
    public void IsListed_FollowsTheIssuersPresentationLine_NotTheClassFiledOn()
    {
        // A SPAC's class A converted into the operating company's shares: the filed class is
        // inactive, the issuer trades, so the stake is not retired.
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "IMSR",
            SecondaryTickers: ["HOND"]
        );
        foreach (
            var listing in issuer
                .Securities.SelectMany(security => security.Listings)
                .Where(listing => listing.Ticker == "HOND")
        )
            listing.Active = false;

        Schedule13DGStatus.IsListed(issuer).Should().BeTrue();
        Schedule13DGStatus
            .IsListed(Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "GONE", Active: false))
            .Should()
            .BeFalse();
        Schedule13DGStatus.IsListed(new EquityIssuer()).Should().BeFalse();
        Schedule13DGStatus.IsListed(null).Should().BeFalse();
    }

    [Fact]
    public void IsHeld_KeepsCurrentAndUnconfirmedOnly()
    {
        Schedule13DGStatus.IsHeld(Schedule13DGStakeStatus.Current).Should().BeTrue();
        Schedule13DGStatus.IsHeld(Schedule13DGStakeStatus.Unconfirmed).Should().BeTrue();
        Schedule13DGStatus.IsHeld(Schedule13DGStakeStatus.BelowThreshold).Should().BeFalse();
        Schedule13DGStatus.IsHeld(Schedule13DGStakeStatus.Exited).Should().BeFalse();
        Schedule13DGStatus.IsHeld(Schedule13DGStakeStatus.SecurityRetired).Should().BeFalse();
    }
}
