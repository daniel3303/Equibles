using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Holdings.Intelligence;

public class HolderMixCalculatorTests
{
    private static readonly DateOnly Previous = new(2025, 9, 30);
    private static readonly DateOnly Current = new(2025, 12, 31);

    private static readonly Guid IndexFund = Id(1);
    private static readonly Guid StockPicker = Id(2);
    private static readonly Guid NewBuyer = Id(3);
    private static readonly Guid Seller = Id(4);
    private static readonly Guid FirstFiler = Id(5);
    private static readonly Guid LateFiler = Id(6);
    private static readonly Guid Unprofiled = Id(7);

    [Fact]
    public void Build_EveryDimensionSumsToTheTotal()
    {
        var quarter = Build(Status(missingPrevious: [FirstFiler], missingCurrent: [LateFiler]));

        quarter.Total.Shares.Should().Be(1_100 + 300 + 50 + 400 + 70);
        foreach (var (dimension, buckets) in quarter.Dimensions)
        {
            buckets.Sum(b => b.Shares).Should().Be(quarter.Total.Shares, dimension.ToString());
            buckets.Sum(b => b.Value).Should().Be(quarter.Total.Value, dimension.ToString());
            buckets.Sum(b => b.Filers).Should().Be(quarter.Total.Filers, dimension.ToString());
            buckets
                .Sum(b => b.CallShares)
                .Should()
                .Be(quarter.Total.CallShares, dimension.ToString());
            buckets
                .Sum(b => b.PutShares)
                .Should()
                .Be(quarter.Total.PutShares, dimension.ToString());
            buckets
                .Sum(b => b.LikeForLikeCurrent)
                .Should()
                .Be(quarter.Total.LikeForLikeCurrent, dimension.ToString());
            buckets
                .Sum(b => b.LikeForLikePrevious)
                .Should()
                .Be(quarter.Total.LikeForLikePrevious, dimension.ToString());
            buckets
                .Sum(b => b.ExcludedCurrentShares)
                .Should()
                .Be(quarter.Total.ExcludedCurrentShares, dimension.ToString());
            buckets
                .Sum(b => b.ExcludedPreviousShares)
                .Should()
                .Be(quarter.Total.ExcludedPreviousShares, dimension.ToString());
            buckets
                .Sum(b => b.NewFilers)
                .Should()
                .Be(quarter.Total.NewFilers, dimension.ToString());
            buckets
                .Sum(b => b.ExitedFilers)
                .Should()
                .Be(quarter.Total.ExitedFilers, dimension.ToString());
        }
    }

    [Fact]
    public void Build_ComparesFilersObservedOnBothSidesInTheirCurrentBucket()
    {
        var quarter = Build(Status(missingPrevious: [FirstFiler], missingCurrent: [LateFiler]));

        // Index fund 1,000 -> 1,100; stock picker 200 -> 300 plus a new buyer at 50 and a seller
        // that exited 500; the first filer and the late filer stay out of the change.
        quarter.Total.LikeForLikeCurrent.Should().Be(1_100 + 300 + 50 + 70);
        quarter.Total.LikeForLikePrevious.Should().Be(1_000 + 200 + 500 + 60);
        quarter.Total.ExcludedCurrentShares.Should().Be(400);
        quarter.Total.ExcludedPreviousShares.Should().Be(900);
        quarter.Total.NewFilers.Should().Be(1);
        quarter.Total.ExitedFilers.Should().Be(1);

        var styles = quarter.Dimensions[HolderMixDimension.Style].ToDictionary(b => b.Token);
        styles["idx"].LikeForLikeCurrent.Should().Be(1_100);
        styles["idx"].LikeForLikePrevious.Should().Be(1_000);
        styles["conc"].LikeForLikeCurrent.Should().Be(300 + 50);
        styles["conc"].LikeForLikePrevious.Should().Be(200 + 500);
        styles["conc"].NewFilers.Should().Be(1);
        styles["conc"].ExitedFilers.Should().Be(1);
        // A filer with no measurement in the measured quarter is not measured, never Unknown.
        styles["none"].Shares.Should().Be(400 + 70);
        styles.Should().NotContainKey("unk");
    }

    [Fact]
    public void ActiveHolders_SumsTheActivelyManagedStyleBucketsOnly()
    {
        var quarter = Build(Status(missingPrevious: [FirstFiler], missingCurrent: [LateFiler]));

        var active = HolderMixCalculator.ActiveHolders(quarter);

        // The concentrated picker, buyer and seller plus the diversified late filer; the index
        // fund and the unmeasured filers are left out.
        active.Shares.Should().Be(300 + 50);
        active.LikeForLikeCurrent.Should().Be(300 + 50);
        active.LikeForLikePrevious.Should().Be(200 + 500);
        active.ExcludedPreviousShares.Should().Be(900);
        active.PutShares.Should().Be(40);
        active.CallShares.Should().Be(0);
        active.NewFilers.Should().Be(1);
        active.ExitedFilers.Should().Be(1);
        HolderMixCalculator
            .ActiveHolders(new HolderMixQuarter { ReportDate = Current, Total = quarter.Total })
            .Should()
            .BeNull("no quarter is measured on or before it");
    }

    [Fact]
    public void Build_UnprofiledFilerReadsUnknownTypeAndLocationFromItsAddressCode()
    {
        var quarter = Build(Status());

        var types = quarter.Dimensions[HolderMixDimension.EntityType].ToDictionary(b => b.Token);
        types["unk"].Shares.Should().Be(70);
        types["adv"].Shares.Should().Be(1_100 + 300 + 50 + 400);
        quarter.Dimensions[HolderMixDimension.EntityType].Last().Token.Should().Be("unk");
        var locations = quarter.Dimensions[HolderMixDimension.Location].ToDictionary(b => b.Token);
        // X0 is the United Kingdom; X1 is the United States given as a country.
        locations["nonus"].Shares.Should().Be(1_100);
        locations["us"].Shares.Should().Be(300 + 50 + 400);
        locations["unk"].Shares.Should().Be(70);
        var privateFunds = quarter
            .Dimensions[HolderMixDimension.PrivateFunds]
            .ToDictionary(b => b.Token);
        privateFunds["hedge"].Shares.Should().Be(300 + 50);
        privateFunds["nopf"].Shares.Should().Be(1_100 + 400);
        privateFunds["unk"].Shares.Should().Be(70);
        var sizes = quarter.Dimensions[HolderMixDimension.BookSize].ToDictionary(b => b.Token);
        sizes["over100b"].Shares.Should().Be(1_100);
        sizes["1to10b"].Shares.Should().Be(300 + 50);
        sizes["none"].Shares.Should().Be(400 + 70);
    }

    [Fact]
    public void Build_OptionsStayOutOfTheSharesAndCountSeparately()
    {
        var quarter = Build(Status());

        quarter.Total.CallShares.Should().Be(25);
        quarter.Total.PutShares.Should().Be(40);
        quarter.Total.Filers.Should().Be(5);
        quarter
            .Dimensions[HolderMixDimension.Style]
            .Single(b => b.Token == "conc")
            .PutShares.Should()
            .Be(40);
    }

    [Fact]
    public void Build_CombinedViewCarriesFilersYetToFileOnBothSides()
    {
        var quarter = Build(Status(missingCurrent: [LateFiler]), combined: true);

        quarter.IsCombined.Should().BeTrue();
        quarter.Total.Shares.Should().Be(1_100 + 300 + 50 + 400 + 70 + 900);
        quarter.Total.LikeForLikeCurrent.Should().Be(1_100 + 300 + 50 + 400 + 70 + 900);
        quarter.Total.LikeForLikePrevious.Should().Be(1_000 + 200 + 500 + 60 + 900);
        quarter.Total.ExcludedPreviousShares.Should().Be(0);
        quarter.Comparison.MissingCurrentFilers.Should().BeEmpty();
        quarter.Total.NewFilers.Should().Be(2);
    }

    [Fact]
    public void Build_WithoutConsecutiveQuartersHasNoChange()
    {
        var quarter = Build(
            new HoldingsComparisonStatus(
                Current,
                new DateOnly(2025, 3, 31),
                new HashSet<Guid>(),
                new HashSet<Guid>()
            ),
            previousDate: new DateOnly(2025, 3, 31)
        );

        quarter.Total.LikeForLikeCurrent.Should().BeNull();
        quarter
            .Dimensions[HolderMixDimension.Style]
            .Should()
            .OnlyContain(b => b.LikeForLikePrevious == null);
    }

    [Fact]
    public void Build_SingleSuccessorCarriesItsPredecessorsPositionInItsOwnBucket()
    {
        var predecessor = Id(20);
        var successor = Id(21);
        var holders = new Dictionary<Guid, HolderMixHolder>
        {
            [predecessor] = Holder(HolderEntityType.Unknown, style: null),
            [successor] = Holder(
                HolderEntityType.RegisteredAdviser,
                HolderStyleBand.IndexLike,
                aum: 5_000_000_000
            ),
        };
        var status = new HoldingsComparisonStatus(
            Current,
            Previous,
            new HashSet<Guid>(),
            new HashSet<Guid>()
        )
        {
            Successions = [Group(Current, [predecessor], [successor])],
        };

        var quarter = Build(
            status,
            positions:
            [
                Position(Previous, predecessor, 1_000),
                Position(Current, successor, 1_050),
            ],
            holders: holders
        );

        var styles = quarter.Dimensions[HolderMixDimension.Style].ToDictionary(b => b.Token);
        styles["idx"].LikeForLikeCurrent.Should().Be(1_050);
        styles["idx"].LikeForLikePrevious.Should().Be(1_000);
        styles.Should().NotContainKey("none");
        quarter.Total.NewFilers.Should().Be(0);
        quarter.Total.ExitedFilers.Should().Be(0);
    }

    [Fact]
    public void Build_SplitSuccessionDividesThePredecessorsPositionByWhatEachSuccessorHolds()
    {
        var predecessor = Id(30);
        var large = Id(31);
        var small = Id(32);
        var holders = new Dictionary<Guid, HolderMixHolder>
        {
            [predecessor] = Holder(HolderEntityType.Unknown, style: null),
            [large] = Holder(
                HolderEntityType.RegisteredAdviser,
                HolderStyleBand.IndexLike,
                aum: 9_000
            ),
            [small] = Holder(HolderEntityType.Bank, HolderStyleBand.Concentrated, aum: 1_000),
        };
        var status = new HoldingsComparisonStatus(
            Current,
            Previous,
            new HashSet<Guid>(),
            new HashSet<Guid>()
        )
        {
            Successions = [Group(Current, [predecessor], [large, small])],
        };

        var quarter = Build(
            status,
            positions:
            [
                Position(Previous, predecessor, 900),
                Position(Current, large, 200),
                Position(Current, small, 100),
            ],
            holders: holders
        );

        var types = quarter.Dimensions[HolderMixDimension.EntityType].ToDictionary(b => b.Token);
        types["adv"].LikeForLikePrevious.Should().Be(600);
        types["bank"].LikeForLikePrevious.Should().Be(300);
        types.Should().NotContainKey("unk");
        quarter.Total.ExitedFilers.Should().Be(0);
    }

    [Fact]
    public void Build_SplitSuccessionThatSoldEverythingExitsInTheLargestSuccessorsBucket()
    {
        // The larger book has the larger id, so only the book size can pick it.
        var predecessor = Id(40);
        var large = Id(42);
        var small = Id(41);
        var holders = new Dictionary<Guid, HolderMixHolder>
        {
            [predecessor] = Holder(HolderEntityType.Unknown, style: null),
            [large] = Holder(
                HolderEntityType.RegisteredAdviser,
                HolderStyleBand.IndexLike,
                aum: 9_000
            ),
            [small] = Holder(HolderEntityType.Bank, HolderStyleBand.Concentrated, aum: 1_000),
        };
        var status = new HoldingsComparisonStatus(
            Current,
            Previous,
            new HashSet<Guid>(),
            new HashSet<Guid>()
        )
        {
            Successions = [Group(Current, [predecessor], [large, small])],
        };

        var quarter = Build(
            status,
            positions: [Position(Previous, predecessor, 900)],
            holders: holders
        );

        var types = quarter.Dimensions[HolderMixDimension.EntityType].ToDictionary(b => b.Token);
        types["adv"].LikeForLikePrevious.Should().Be(900);
        types["adv"].LikeForLikeCurrent.Should().Be(0);
        types["adv"].ExitedFilers.Should().Be(1);
        types.Should().NotContainKey("bank");
    }

    [Theory]
    [InlineData(10L, new long[] { 1, 1, 1 }, new long[] { 4, 3, 3 })]
    [InlineData(900L, new long[] { 200, 100 }, new long[] { 600, 300 })]
    [InlineData(7L, new long[] { 0, 5 }, new long[] { 0, 7 })]
    [InlineData(5L, new long[] { 0, 0 }, new long[] { 0, 0 })]
    public void Allocate_SplitsExactlyInProportion(long amount, long[] weights, long[] expected)
    {
        HolderMixCalculator.Allocate(amount, weights).Should().Equal(expected);
    }

    // Index fund (non-US, adviser, no private funds, $150B book) 1,000 -> 1,100; stock picker (US
    // hedge-fund adviser, $5B) 200 -> 300 with puts; a new buyer and a seller with the picker's
    // profile; a first filer with no previous 13F; a late filer with no current 13F; an unprofiled
    // and unmeasured filer 60 -> 70 with calls.
    private static HolderMixQuarter Build(
        HoldingsComparisonStatus status,
        bool combined = false,
        DateOnly? previousDate = null,
        List<HolderMixPosition> positions = null,
        Dictionary<Guid, HolderMixHolder> holders = null
    )
    {
        var before = previousDate ?? Previous;
        positions ??=
        [
            Position(before, IndexFund, 1_000),
            Position(Current, IndexFund, 1_100),
            Position(before, StockPicker, 200),
            Position(Current, StockPicker, 300, puts: 40),
            Position(Current, NewBuyer, 50),
            Position(before, Seller, 500),
            Position(Current, Seller, 0, calls: 0),
            Position(Current, FirstFiler, 400),
            Position(before, LateFiler, 900),
            Position(before, Unprofiled, 60),
            Position(Current, Unprofiled, 70, calls: 25),
        ];
        holders ??= new Dictionary<Guid, HolderMixHolder>
        {
            [IndexFund] = Holder(
                HolderEntityType.RegisteredAdviser,
                HolderStyleBand.IndexLike,
                aum: 150_000_000_000,
                location: "X0",
                privateFunds: false
            ),
            [StockPicker] = Picker(),
            [NewBuyer] = Picker(),
            [Seller] = Picker(),
            [FirstFiler] = Holder(
                HolderEntityType.RegisteredAdviser,
                style: null,
                location: "X1",
                privateFunds: false
            ),
            [LateFiler] = Holder(
                HolderEntityType.RegisteredAdviser,
                HolderStyleBand.DiversifiedActive,
                aum: 2_000_000_000,
                location: "CA",
                privateFunds: false
            ),
            [Unprofiled] = new HolderMixHolder(),
        };
        return HolderMixCalculator
            .Build(
                [before, Current],
                positions,
                new Dictionary<DateOnly, HoldingsComparisonStatus> { [Current] = status },
                new Dictionary<DateOnly, DateOnly?> { [Current] = Current },
                (holderId, _, _) => holders[holderId],
                combined ? Current : null
            )
            .Single();
    }

    private static HolderMixHolder Picker() =>
        Holder(
            HolderEntityType.RegisteredAdviser,
            HolderStyleBand.Concentrated,
            aum: 5_000_000_000,
            location: "NY",
            privateFunds: true,
            focus: PrivateFundFocus.Hedge
        );

    private static HolderMixHolder Holder(
        HolderEntityType type,
        HolderStyleBand? style,
        long? aum = null,
        string location = null,
        bool? privateFunds = null,
        PrivateFundFocus focus = PrivateFundFocus.None
    ) =>
        new()
        {
            Profiled = true,
            EntityType = type,
            StyleBand = style,
            TurnoverBand = style == null ? null : HolderTurnoverBand.Low,
            HorizonBand = style == null ? null : HolderHorizonBand.Long,
            EquityAum = style == null ? null : aum,
            StateOrCountry = location,
            AdvisesPrivateFunds = privateFunds,
            PrivateFundFocus = focus,
        };

    private static HoldingsComparisonStatus Status(
        IEnumerable<Guid> missingPrevious = null,
        IEnumerable<Guid> missingCurrent = null
    ) =>
        new(
            Current,
            Previous,
            (missingPrevious ?? []).ToHashSet(),
            (missingCurrent ?? []).ToHashSet()
        );

    private static HoldingsSuccessionGroup Group(
        DateOnly date,
        Guid[] predecessors,
        Guid[] successors
    ) =>
        new(
            date,
            predecessors.ToDictionary(id => id, _ => "Predecessor"),
            successors.ToDictionary(id => id, _ => "Successor"),
            true
        );

    private static HolderMixPosition Position(
        DateOnly date,
        Guid holderId,
        long shares,
        long calls = 0,
        long puts = 0
    ) =>
        new()
        {
            ReportDate = date,
            InstitutionalHolderId = holderId,
            Shares = shares,
            Value = shares * 10,
            CallShares = calls,
            CallValue = calls * 10,
            PutShares = puts,
            PutValue = puts * 10,
        };

    private static Guid Id(int value) => new($"00000000-0000-0000-0000-{value:D12}");
}
