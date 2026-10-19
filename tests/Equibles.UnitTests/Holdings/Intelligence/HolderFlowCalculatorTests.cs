using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.UnitTests.Holdings.Intelligence;

public class HolderFlowCalculatorTests
{
    private static readonly DateOnly Quarter = new(2026, 6, 30);
    private static readonly HolderFlowQuarterState Computed = new(
        Quarter,
        new DateTime(2026, 10, 19, 1, 0, 0, DateTimeKind.Utc),
        HolderSectorFlowCalculator.FirstVersion
    );
    private static readonly Guid Technology = Guid.NewGuid();

    [Fact]
    public void Build_SumsComparedFilersAndCountsTheRest()
    {
        var adviserA = Holder(HolderEntityType.RegisteredAdviser, 1_000, 900, 300m, 100m, 2);
        var adviserB = Holder(HolderEntityType.RegisteredAdviser, 500, 600, 50m, 150m, 0);
        var newBank = Holder(HolderEntityType.Bank, 800, null, null, null, null);

        var flows = HolderFlowCalculator.Build(
            Computed,
            HolderMixDimension.EntityType,
            HolderFlowGrouping.None,
            [adviserA, adviserB, newBank]
        );

        var advisers = flows.Buckets.Single(bucket => bucket.Token == "adv");
        advisers.Filers.Should().Be(2);
        advisers.ComparedFilers.Should().Be(2);
        advisers.BuyValue.Should().Be(350m);
        advisers.SellValue.Should().Be(250m);
        advisers.NetValue.Should().Be(100m);
        advisers.PriorEquityAum.Should().Be(1_500);
        advisers.NetPercentOfPriorBook.Should().Be(100m / 1_500m * 100m);
        advisers.NetBuyers.Should().Be(1);
        advisers.NetSellers.Should().Be(1);
        advisers.UnpricedTrades.Should().Be(2);

        // A filer with no 13F the quarter before is counted but adds no flow or prior book.
        var banks = flows.Buckets.Single(bucket => bucket.Token == "bank");
        banks.Filers.Should().Be(1);
        banks.ComparedFilers.Should().Be(0);
        banks.EquityAum.Should().Be(800);
        banks.NetValue.Should().Be(0m);
        banks.NetPercentOfPriorBook.Should().BeNull();

        flows.Total.Token.Should().Be(HolderFlowCalculator.TotalToken);
        flows.Total.Filers.Should().Be(3);
        flows.Total.ComparedFilers.Should().Be(2);
        flows.Total.NetValue.Should().Be(100m);
        flows.Total.EquityAum.Should().Be(2_300);
        flows.Total.Sectors.Should().BeEmpty();
    }

    [Fact]
    public void Build_BySector_SplitsEachBucketAndOrdersSectorsThenProductsThenUnclassified()
    {
        var adviser = Holder(HolderEntityType.RegisteredAdviser, 1_000, 900, 300m, 100m, 0);
        var bank = Holder(HolderEntityType.Bank, 500, 600, 50m, 150m, 0);
        var rows = new[]
        {
            new HolderFlowSectorRow(
                adviser.InstitutionalHolderId,
                HolderFlowSectorGroup.Unclassified,
                null,
                10m,
                0m
            ),
            new HolderFlowSectorRow(
                adviser.InstitutionalHolderId,
                HolderFlowSectorGroup.ExchangeTraded,
                null,
                90m,
                100m
            ),
            new HolderFlowSectorRow(
                adviser.InstitutionalHolderId,
                HolderFlowSectorGroup.Sector,
                Technology,
                200m,
                0m
            ),
            new HolderFlowSectorRow(
                bank.InstitutionalHolderId,
                HolderFlowSectorGroup.Sector,
                Technology,
                50m,
                150m
            ),
        }.ToLookup(row => row.InstitutionalHolderId);

        var flows = HolderFlowCalculator.Build(
            Computed,
            HolderMixDimension.EntityType,
            HolderFlowGrouping.Sector,
            [adviser, bank],
            rows,
            new Dictionary<Guid, string> { [Technology] = "Technology" }
        );

        flows
            .Buckets.Single(bucket => bucket.Token == "adv")
            .Sectors.Select(sector => (sector.Name, sector.NetValue))
            .Should()
            .Equal(
                ("Technology", 200m),
                (HolderFlowCalculator.ExchangeTradedLabel, -10m),
                (HolderFlowCalculator.UnclassifiedLabel, 10m)
            );
        flows
            .Total.Sectors.Single(sector => sector.Group == HolderFlowSectorGroup.Sector)
            .Should()
            .BeEquivalentTo(
                new
                {
                    BuyValue = 250m,
                    SellValue = 150m,
                    NetValue = 100m,
                }
            );
    }

    [Fact]
    public void Build_BySector_BeforeTheQuarterCarriesSectorRows_ReadsTotalsOnly()
    {
        var adviser = Holder(HolderEntityType.RegisteredAdviser, 1_000, 900, 300m, 100m, 0);
        var olderVersion = Computed with
        {
            CalculationVersion = HolderSectorFlowCalculator.FirstVersion - 1,
        };

        var flows = HolderFlowCalculator.Build(
            olderVersion,
            HolderMixDimension.EntityType,
            HolderFlowGrouping.Sector,
            [adviser],
            new[]
            {
                new HolderFlowSectorRow(
                    adviser.InstitutionalHolderId,
                    HolderFlowSectorGroup.Sector,
                    Technology,
                    300m,
                    100m
                ),
            }.ToLookup(row => row.InstitutionalHolderId)
        );

        flows.SectorSplitAvailable.Should().BeFalse();
        flows.Total.NetValue.Should().Be(200m);
        flows.Total.Sectors.Should().BeEmpty();
    }

    [Fact]
    public void Build_BucketsFilersByTheQuartersOwnMeasurements()
    {
        var indexLike = Holder(HolderEntityType.RegisteredAdviser, 1_000, 900, 300m, 100m, 0) with
        {
            Mix = HolderMixHolder.Of(null, HolderStyleBand.IndexLike, null, null, 1_000, null),
        };

        var flows = HolderFlowCalculator.Build(
            Computed,
            HolderMixDimension.Style,
            HolderFlowGrouping.None,
            [indexLike]
        );

        flows.Buckets.Should().ContainSingle().Which.Token.Should().Be("idx");
    }

    private static HolderFlowHolder Holder(
        HolderEntityType type,
        long equityAum,
        long? previousEquityAum,
        decimal? buy,
        decimal? sell,
        int? unpriced
    ) =>
        new()
        {
            InstitutionalHolderId = Guid.NewGuid(),
            Mix = HolderMixHolder.Of(
                new HolderMixProfile(type, HolderClientBase.Unknown, null, PrivateFundFocus.None),
                null,
                null,
                null,
                equityAum,
                null
            ),
            EquityAum = equityAum,
            PreviousEquityAum = previousEquityAum,
            BuyValue = buy,
            SellValue = sell,
            UnpricedTradeCount = unpriced,
        };
}
