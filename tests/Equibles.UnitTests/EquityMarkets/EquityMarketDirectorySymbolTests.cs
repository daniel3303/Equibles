using Equibles.EquityMarkets.BusinessLogic.Directory;

namespace Equibles.UnitTests.EquityMarkets;

public class EquityMarketDirectorySymbolTests
{
    // Each row is a venue's own spelling beside the spelling the site route and the price provider use.
    [Theory]
    [InlineData("VOLV B", "VOLV-B")]
    [InlineData("ALIV SDB", "ALIV-SDB")]
    [InlineData("BESQAB PREF B", "BESQAB-PREF-B")]
    [InlineData("MAERSK A", "MAERSK-A")]
    [InlineData("GRF.P", "GRF-P")]
    [InlineData("BT.A", "BT-A")]
    [InlineData("RR.", "RR")]
    [InlineData("BRK-B", "BRK-B")]
    [InlineData("AAK", "AAK")]
    [InlineData(" 11b ", "11B")]
    public void Normalize_WritesTheShareClassSeparatorAsAHyphen(string venue, string expected)
    {
        EquityMarketDirectorySymbol.Normalize(venue).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("VOLV  B")]
    [InlineData("A/B")]
    [InlineData("ÅF")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456")]
    public void Normalize_RefusesASymbolOutsideTheListingContract(string venue)
    {
        EquityMarketDirectorySymbol.Normalize(venue).Should().BeNull();
    }
}
