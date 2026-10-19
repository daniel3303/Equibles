using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.UnitTests.Holdings.Intelligence;

public class HolderSectorFlowCalculatorTests
{
    private static readonly Guid Holder = Guid.NewGuid();
    private static readonly DateOnly Quarter = new(2026, 6, 30);
    private static readonly Guid Technology = Guid.NewGuid();
    private static readonly Guid Financials = Guid.NewGuid();

    [Fact]
    public void Flows_SumsEachSectorAndKeepsUntradedSectorsOut()
    {
        var chips = Guid.NewGuid();
        var software = Guid.NewGuid();
        var bank = Guid.NewGuid();
        var noSector = Guid.NewGuid();
        var sectors = new Dictionary<Guid, Guid>
        {
            [chips] = Technology,
            [software] = Technology,
            [bank] = Financials,
        };

        var flows = HolderSectorFlowCalculator.Flows(
            Holder,
            Quarter,
            [
                new(new(chips, null), 300m, 0m),
                new(new(software, null), 0m, 100m),
                new(new(noSector, null), 50m, 0m),
                new(new(bank, null), 0m, 0m),
            ],
            sectors,
            new HashSet<HolderSecurityKey>()
        );

        flows
            .Select(flow => (flow.SectorGroup, flow.SectorId, flow.BuyValue, flow.SellValue))
            .Should()
            .BeEquivalentTo([
                (HolderFlowSectorGroup.Sector, (Guid?)Technology, 300m, 100m),
                (HolderFlowSectorGroup.Unclassified, (Guid?)null, 50m, 0m),
            ]);
        flows.Should().OnlyContain(flow => flow.InstitutionalHolderId == Holder);
        flows.Should().OnlyContain(flow => flow.ReportDate == Quarter);
    }

    [Fact]
    public void Flows_AnExchangeTradedListingLeavesItsIssuersOtherClassesInTheirSector()
    {
        // A bank that issues an ETN: the note is an exchange-traded product, its common is not.
        var bank = Guid.NewGuid();
        var common = new HolderSecurityKey(bank, null);
        var note = new HolderSecurityKey(bank, "BKETN");

        var flows = HolderSectorFlowCalculator.Flows(
            Holder,
            Quarter,
            [new(common, 100m, 0m), new(note, 0m, 40m)],
            new Dictionary<Guid, Guid> { [bank] = Financials },
            new HashSet<HolderSecurityKey> { note }
        );

        flows
            .Select(flow => (flow.SectorGroup, flow.SectorId, flow.BuyValue, flow.SellValue))
            .Should()
            .BeEquivalentTo([
                (HolderFlowSectorGroup.Sector, (Guid?)Financials, 100m, 0m),
                (HolderFlowSectorGroup.ExchangeTraded, (Guid?)null, 0m, 40m),
            ]);
    }

    [Fact]
    public void Build_SectorRowsFoldTheFilersPricedTrades()
    {
        var issuer = Guid.NewGuid();
        var input = new HolderQuarterInput
        {
            InstitutionalHolderId = Holder,
            ReportDate = Quarter,
            Current = [Row(issuer, Quarter, 150, 1_500)],
            Previous = [Row(issuer, Quarter.AddMonths(-3), 100, 900)],
            IssuerSectors = new Dictionary<Guid, Guid> { [issuer] = Technology },
        };

        var result = HolderQuarterMetricsBuilder.Build(input, 4);

        result
            .SectorFlows.Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    InstitutionalHolderId = Holder,
                    ReportDate = Quarter,
                    SectorGroup = HolderFlowSectorGroup.Sector,
                    SectorId = (Guid?)Technology,
                    BuyValue = result.Metrics.BuyValue.Value,
                    SellValue = 0m,
                }
            );
        result.Metrics.BuyValue.Should().Be(500m);
    }

    [Fact]
    public void Build_NoFilingTheQuarterBefore_HasNoSectorRows()
    {
        var issuer = Guid.NewGuid();
        var input = new HolderQuarterInput
        {
            InstitutionalHolderId = Holder,
            ReportDate = Quarter,
            Current = [Row(issuer, Quarter, 150, 1_500)],
            IssuerSectors = new Dictionary<Guid, Guid> { [issuer] = Technology },
        };

        HolderQuarterMetricsBuilder.Build(input, 4).SectorFlows.Should().BeEmpty();
    }

    private static HolderIntelligenceRow Row(
        Guid issuer,
        DateOnly reportDate,
        long shares,
        long value
    ) =>
        new()
        {
            InstitutionalHolderId = Holder,
            EquityIssuerId = issuer,
            ReportDate = reportDate,
            ShareType = ShareType.Shares,
            Shares = shares,
            Value = value,
        };
}
