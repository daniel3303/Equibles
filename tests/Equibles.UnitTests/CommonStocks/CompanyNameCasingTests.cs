using Equibles.CommonStocks.Data.Helpers;

namespace Equibles.UnitTests.CommonStocks;

public class CompanyNameCasingTests
{
    [Theory]
    [InlineData("VANGUARD INDEX FUNDS", "Vanguard Index Funds")]
    [InlineData("SPDR SERIES TRUST", "Spdr Series Trust")]
    [InlineData("INVESCO EXCHANGE-TRADED FUND TRUST II", "Invesco Exchange-Traded Fund Trust II")]
    [InlineData("iShares Trust", "iShares Trust")]
    public void Normalize_TitleCasesOnlyAllCapsNames(string name, string expected)
    {
        CompanyNameCasing.Normalize(name).Should().Be(expected);
    }
}
