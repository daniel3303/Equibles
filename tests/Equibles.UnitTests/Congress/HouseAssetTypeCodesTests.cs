using Equibles.Congress.Data.Models;

namespace Equibles.UnitTests.Congress;

public class HouseAssetTypeCodesTests
{
    [Theory]
    [InlineData("ST", "Stocks (including ADRs)")]
    [InlineData("OP", "Options")]
    [InlineData("Stock", "Stock")]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void Display_NamesAHouseCodeAndKeepsAnyOtherValue(string stored, string expected)
    {
        HouseAssetTypeCodes.Display(stored).Should().Be(expected);
    }
}
