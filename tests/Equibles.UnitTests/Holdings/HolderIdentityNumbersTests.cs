using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HolderIdentityNumbersTests
{
    [Fact]
    public void OwnNumber_Wins()
    {
        HolderIdentityNumbers
            .Resolve(" 000105958 ", "000999999", ["000999999"])
            .Should()
            .Be("000105958");
    }

    [Fact]
    public void BlankCoverNumber_KeepsTheStoredOne()
    {
        HolderIdentityNumbers.Resolve(null, "000105958", []).Should().Be("000105958");
    }

    [Fact]
    public void BlankCoverNumber_DropsAStoredNumberTheCoverListsForAnotherManager()
    {
        // The old parser read a combination report's parent numbers when the filer left its own
        // element blank; a stored value equal to a listed manager's number is that mistake.
        HolderIdentityNumbers.Resolve("", "000000361", [null, "000000361"]).Should().BeNull();
    }

    [Fact]
    public void BlankStoredNumber_IsAbsent()
    {
        HolderIdentityNumbers.Resolve(null, "  ", []).Should().BeNull();
    }
}
