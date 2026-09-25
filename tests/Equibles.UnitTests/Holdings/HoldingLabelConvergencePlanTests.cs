using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using static Equibles.Holdings.HostedService.Services.HoldingLabelConvergenceService;

namespace Equibles.UnitTests.Holdings;

// Pins which stored labels the convergence pass may move, and that the importer's CUSIP
// resolution never labels a position with its issuer's own presentation ticker.
public class HoldingLabelConvergencePlanTests
{
    private static readonly Guid Issuer = Guid.NewGuid();
    private static readonly Guid OtherIssuer = Guid.NewGuid();
    private static readonly Dictionary<Guid, CandidateIssuer> Presentation = new()
    {
        [Issuer] = new CandidateIssuer
        {
            Id = Issuer,
            PresentationTicker = "BF-B",
            PresentationIdentified = true,
            LiveTickers = ["BF-B", "BF-A"],
        },
    };

    [Fact]
    public void Plan_PresentationLabelWithAPrimaryCusip_MovesToPrimary()
    {
        var moves = Plan(
            [Label("115637209", "BF-B")],
            Presentation,
            Mapping(("115637209", Issuer, null))
        );

        moves
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(new Relabel(Issuer, "115637209", "BF-B", null, 1));
    }

    [Fact]
    public void Plan_PresentationLabelWithAnUnresolvedCusip_MovesToPrimary()
    {
        var moves = Plan(
            [Label(null, "BF-B"), Label("000000000", "BF-B")],
            Presentation,
            Mapping()
        );

        moves.Should().HaveCount(2).And.OnlyContain(move => move.ToTicker == null);
    }

    [Fact]
    public void Plan_PresentationLabelWhoseCusipNamesASibling_StaysPut()
    {
        var moves = Plan(
            [Label("115637100", "BF-B")],
            Presentation,
            Mapping(("115637100", Issuer, "BF-A"))
        );

        moves.Should().BeEmpty();
    }

    [Fact]
    public void Plan_PrimaryRowOfASiblingCusip_MovesToTheSiblingBeforeAnyPrimaryMove()
    {
        var moves = Plan(
            [Label("115637209", "BF-B"), Label("115637100", null)],
            Presentation,
            Mapping(("115637209", Issuer, null), ("115637100", Issuer, "BF-A"))
        );

        moves.Select(move => move.ToTicker).Should().Equal("BF-A", null);
    }

    [Fact]
    public void Plan_PrimaryRowOfASiblingCusipBesideAnUnidentifiedPresentation_StaysPut()
    {
        // HOST replaced HCWC as a new listing with no CUSIP: the old CUSIP's rows are the same
        // security renamed, so they must not move onto the retired symbol.
        var moves = Plan(
            [Label("42227T105", null)],
            new Dictionary<Guid, CandidateIssuer>
            {
                [Issuer] = new CandidateIssuer
                {
                    Id = Issuer,
                    PresentationTicker = "HOST",
                    LiveTickers = ["HOST", "HCWC"],
                },
            },
            Mapping(("42227T105", Issuer, "HCWC"))
        );

        moves.Should().BeEmpty();
    }

    [Fact]
    public void Plan_PrimaryRowOfARetiredSiblingCusip_StaysPut()
    {
        // IDXG's pre-split CUSIP sits on its delisted listing; IDXGD is the same share class.
        var moves = Plan(
            [Label("46062X303", null)],
            new Dictionary<Guid, CandidateIssuer>
            {
                [Issuer] = new CandidateIssuer
                {
                    Id = Issuer,
                    PresentationTicker = "IDXGD",
                    PresentationIdentified = true,
                    LiveTickers = ["IDXGD"],
                },
            },
            Mapping(("46062X303", Issuer, "IDXG"))
        );

        moves.Should().BeEmpty();
    }

    [Fact]
    public void Plan_PrimaryRowWhoseCusipBelongsToAnotherIssuer_StaysPut()
    {
        var moves = Plan(
            [Label("115637100", null)],
            Presentation,
            Mapping(("115637100", OtherIssuer, "XYZ"))
        );

        moves.Should().BeEmpty();
    }

    [Fact]
    public void Plan_SiblingLabelAndPrimaryRowOfPrimaryCusip_StayPut()
    {
        var moves = Plan(
            [Label("115637100", "BF-A"), Label("115637209", null)],
            Presentation,
            Mapping(("115637100", Issuer, "BF-A"), ("115637209", Issuer, null))
        );

        moves.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_ListingClaimNamingThePresentationTicker_ResolvesToPrimary()
    {
        var mapping = HoldingCusipResolution.Resolve(
            new HoldingCusipResolution.Claims
            {
                Listed =
                [
                    new HoldingCusipResolution.ListedClaim
                    {
                        EquityIssuerId = Issuer,
                        ListedTicker = "BEAT",
                        Cusip = "42238H108",
                        PresentationTicker = "BEAT",
                    },
                    new HoldingCusipResolution.ListedClaim
                    {
                        EquityIssuerId = Issuer,
                        ListedTicker = "BEATW",
                        Cusip = "42238H116",
                        PresentationTicker = "BEAT",
                    },
                ],
            },
            []
        );

        mapping["42238H108"].Should().Be(new CusipTarget(Issuer, null));
        mapping["42238H116"].Should().Be(new CusipTarget(Issuer, "BEATW"));
    }

    [Fact]
    public void Resolve_CusipClaimedAsAliasAndListing_IsDroppedAsContested()
    {
        var contested = new List<string>();
        var mapping = HoldingCusipResolution.Resolve(
            new HoldingCusipResolution.Claims
            {
                Listed =
                [
                    new HoldingCusipResolution.ListedClaim
                    {
                        EquityIssuerId = Issuer,
                        ListedTicker = "BF-A",
                        Cusip = "115637100",
                        PresentationTicker = "BF-B",
                    },
                ],
                Aliases =
                [
                    new HoldingCusipResolution.AliasClaim
                    {
                        EquityIssuerId = Issuer,
                        Cusip = "115637100",
                    },
                ],
            },
            contested
        );

        mapping.Should().BeEmpty();
        contested.Should().Equal("115637100");
    }

    [Fact]
    public void Plan_PrimaryRowResolvedToThePresentationTicker_StaysPut()
    {
        // A retired listing reusing the presentation symbol (IDXGD back to IDXG) is the same line.
        var issuers = new Dictionary<Guid, CandidateIssuer>
        {
            [Issuer] = new CandidateIssuer
            {
                Id = Issuer,
                PresentationTicker = "IDXG",
                PresentationIdentified = true,
                LiveTickers = ["IDXG"],
            },
        };

        var moves = Plan(
            [Label("45166V304", null)],
            issuers,
            Mapping(("45166V304", Issuer, "IDXG"))
        );

        moves.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_RetiredSecurityTradingUnderThePresentationTicker_ResolvesToPrimary()
    {
        var primarySecurity = Guid.NewGuid();
        var mapping = HoldingCusipResolution.Resolve(
            new HoldingCusipResolution.Claims
            {
                Securities =
                [
                    new HoldingCusipResolution.SecurityClaim
                    {
                        Id = Guid.NewGuid(),
                        EquityIssuerId = Issuer,
                        Cusip = "45166V304",
                        PrimarySecurityId = primarySecurity,
                        PresentationTicker = "IDXG",
                        UsTickers = ["IDXG"],
                    },
                    new HoldingCusipResolution.SecurityClaim
                    {
                        Id = Guid.NewGuid(),
                        EquityIssuerId = Issuer,
                        Cusip = "45166V114",
                        PrimarySecurityId = primarySecurity,
                        PresentationTicker = "IDXG",
                        UsTickers = ["IDXGW"],
                    },
                ],
            },
            []
        );

        mapping["45166V304"].Should().Be(new CusipTarget(Issuer, null));
        mapping["45166V114"].Should().Be(new CusipTarget(Issuer, "IDXGW"));
    }

    private static StoredLabel Label(string cusip, string ticker) =>
        new()
        {
            EquityIssuerId = Issuer,
            Cusip = cusip,
            ListedTicker = ticker,
            Count = 1,
        };

    private static Dictionary<string, CusipTarget> Mapping(
        params (string Cusip, Guid Issuer, string Ticker)[] targets
    ) =>
        targets.ToDictionary(
            target => target.Cusip,
            target => new CusipTarget(target.Issuer, target.Ticker),
            StringComparer.OrdinalIgnoreCase
        );
}
