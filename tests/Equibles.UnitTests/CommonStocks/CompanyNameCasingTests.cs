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

    // SEC appends a filing suffix to tell same-named registrants apart; it is not part of the name.
    [Theory]
    [InlineData("TOYOTA MOTOR CORP/", "Toyota Motor Corp")]
    [InlineData("Toyota Tsusho Corporation/ADR", "Toyota Tsusho Corporation")]
    [InlineData("CANADIAN IMPERIAL BANK OF COMMERCE /CAN/", "Canadian Imperial Bank Of Commerce")]
    [InlineData("Columbia Financial, Inc./MD/", "Columbia Financial, Inc.")]
    [InlineData("ADVISORY BOARD CO/THE", "Advisory Board Co")]
    [InlineData("CHURCHILL CAPITAL CORP IX/CAYMAN", "Churchill Capital Corp IX")]
    [InlineData("Columbus Acquisition Corp/Cayman Islands", "Columbus Acquisition Corp")]
    [InlineData("THOR INVESTMENT MANAGEMENT INC /OH/ /ADV", "Thor Investment Management Inc")]
    public void Normalize_DropsSecFilingSuffixes(string name, string expected)
    {
        CompanyNameCasing.Normalize(name).Should().Be(expected);
    }

    // A one-letter segment is a legal form, a numbered tail is part of the name, and a name that is
    // only a suffix keeps it.
    [Theory]
    [InlineData("Scandinavian Medical Solutions A/S")]
    [InlineData("/De/")]
    [InlineData("Fund 2008/11")]
    public void Normalize_KeepsSlashesThatAreNotFilingSuffixes(string name)
    {
        CompanyNameCasing.Normalize(name).Should().Be(name);
    }
}
