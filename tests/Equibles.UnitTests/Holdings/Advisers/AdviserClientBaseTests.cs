using Equibles.Holdings.BusinessLogic.Advisers;
using Equibles.Holdings.Data.Models;
using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Holdings.Advisers;

public class AdviserClientBaseTests
{
    [Fact]
    public void GroupsIndividualsAndNeedsHalfTheAssets()
    {
        var (clientBase, share, pooled) = AdviserClientBase.Of([
            (FormAdvClientType.Individuals, 30),
            (FormAdvClientType.HighNetWorthIndividuals, 20),
            (FormAdvClientType.PensionAndProfitSharingPlans, 49),
            (FormAdvClientType.Other, 1),
        ]);

        clientBase.Should().Be(HolderClientBase.Individuals);
        share.Should().Be(0.5m);
        pooled.Should().Be(0m);
    }

    [Fact]
    public void WithoutAMajorityGroup_IsDiversified()
    {
        var (clientBase, share, pooled) = AdviserClientBase.Of([
            (FormAdvClientType.Individuals, 40),
            (FormAdvClientType.PooledInvestmentVehicles, 35),
            (FormAdvClientType.Corporations, 25),
        ]);

        clientBase.Should().Be(HolderClientBase.Diversified);
        share.Should().BeNull();
        pooled.Should().Be(0.35m);
    }

    [Fact]
    public void WithoutReportedAssets_IsUnknown()
    {
        var (clientBase, share, pooled) = AdviserClientBase.Of([
            (FormAdvClientType.Individuals, null),
        ]);

        clientBase.Should().Be(HolderClientBase.Unknown);
        share.Should().BeNull();
        pooled.Should().BeNull();
        AdviserClientBase.Of([]).Base.Should().Be(HolderClientBase.Unknown);
    }
}
