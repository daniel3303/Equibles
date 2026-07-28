using Equibles.Holdings.HostedService.Models;

namespace Equibles.UnitTests.Holdings;

public class UnmappedCusipTallyAddTests
{
    [Fact]
    public void Add_SeveralFilersOnTheSameSecurity_SumsPositionsAndDollars()
    {
        // The dollars are the whole point of the queue: an operator deciding which missing
        // identifier to map next needs to know which one is costing the most, and one filer's
        // position says nothing about that. Hudson Pacific went missing from Scion's Q2 2024
        // filing alone for $5.5M; across every filer holding it, the retired CUSIP was worth far
        // more than that.
        var tally = new UnmappedCusipTally();

        tally.Add("HUDSON PAC PPTYS INC", 5_504_732m);
        tally.Add("HUDSON PAC PPTYS INC", 1_200_000m);

        tally.Positions.Should().Be(2);
        tally.FiledValue.Should().Be(6_704_732L);
        tally.IssuerName.Should().Be("HUDSON PAC PPTYS INC");
    }

    [Fact]
    public void Add_FirstRowsCarryNoName_TakesTheFirstRealOne()
    {
        // The realtime archive has no issuer-name column at all, and bulk rows occasionally file a
        // blank one, so the label has to survive arriving late. Without this the queue lists bare
        // CUSIPs, which is a research task rather than a decision.
        var tally = new UnmappedCusipTally();

        tally.Add(null, 100m);
        tally.Add("   ", 100m);
        tally.Add("HUDSON PAC PPTYS INC", 100m);
        tally.Add("HUDSON PACIFIC PROPERTIES", 100m);

        tally.Positions.Should().Be(4);
        tally.IssuerName.Should().Be("HUDSON PAC PPTYS INC");
    }

    [Fact]
    public void Add_PositionWithNoFiledValue_StillCounts()
    {
        // Schedule 13D/G rows report no value. They are still evidence that an identifier is
        // missing, so they must count as positions even though they add nothing to the ranking.
        var tally = new UnmappedCusipTally();

        tally.Add("SOME ISSUER", 0m);

        tally.Positions.Should().Be(1);
        tally.FiledValue.Should().Be(0L);
    }
}
