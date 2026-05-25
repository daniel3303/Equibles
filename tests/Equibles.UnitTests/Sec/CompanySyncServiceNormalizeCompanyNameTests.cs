using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the SEC company-name casing rule applied at sync time: ALL-CAPS feed
/// values are title-cased on the way in, mixed-case names are trusted as-is.
/// Tested via reflection because the helper is private to CompanySyncService.
/// </summary>
public class CompanySyncServiceNormalizeCompanyNameTests
{
    private static readonly MethodInfo NormalizeMethod = typeof(CompanySyncService).GetMethod(
        "NormalizeCompanyName",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    private static string Normalize(string name) => (string)NormalizeMethod.Invoke(null, [name]);

    [Theory]
    [InlineData("AMAZON COM INC", "Amazon Com Inc")]
    [InlineData("MICROSOFT CORP", "Microsoft Corp")]
    [InlineData("NVIDIA CORP", "Nvidia Corp")]
    [InlineData("BERKSHIRE HATHAWAY INC", "Berkshire Hathaway Inc")]
    [InlineData("GENERAL ELECTRIC CO", "General Electric Co")]
    public void AllCapsName_IsTitleCased(string input, string expected)
    {
        Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("BLACKSTONE GROUP LP", "Blackstone Group LP")]
    [InlineData("APOLLO GLOBAL MANAGEMENT LLC", "Apollo Global Management LLC")]
    [InlineData("BROOKFIELD ASSET MANAGEMENT LLP", "Brookfield Asset Management LLP")]
    [InlineData("BARCLAYS PLC", "Barclays PLC")]
    [InlineData("UNILEVER NV", "Unilever NV")]
    [InlineData("SIEMENS AG", "Siemens AG")]
    [InlineData("SAP SE", "Sap SE")]
    [InlineData("ATLAS COPCO AB", "Atlas Copco AB")]
    [InlineData("EQUINOR ASA", "Equinor ASA")]
    [InlineData("ISHARES MSCI USA ETF", "Ishares Msci USA ETF")]
    [InlineData("BARCLAYS PLC.", "Barclays PLC.")]
    public void CorporateAbbreviations_StayUpperCase(string input, string expected)
    {
        Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("HENRY SCHEIN III", "Henry Schein III")]
    [InlineData("FORD MOTOR IV", "Ford Motor IV")]
    [InlineData("SOME FUND II", "Some Fund II")]
    [InlineData("CAPITAL GROUP IX", "Capital Group IX")]
    [InlineData("VENTURE FUND XV", "Venture Fund XV")]
    [InlineData("SERIES I", "Series I")]
    [InlineData("FUND VI", "Fund VI")]
    [InlineData("TRUST VII", "Trust VII")]
    [InlineData("PARTNERS XIV", "Partners XIV")]
    public void RomanNumerals_StayUpperCase(string input, string expected)
    {
        Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("APOLLO FUND III LP", "Apollo Fund III LP")]
    [InlineData("KKR REAL ESTATE IV LLC", "Kkr Real Estate IV LLC")]
    public void CombinedAbbreviationsAndRomanNumerals_AllStayUpperCase(
        string input,
        string expected
    )
    {
        Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Apple Inc.")]
    [InlineData("Alphabet Inc.")]
    [InlineData("Meta Platforms, Inc.")]
    [InlineData("JPMorgan Chase & Co.")]
    public void MixedCaseName_IsLeftAlone(string input)
    {
        Normalize(input).Should().Be(input);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrWhitespace_IsLeftAlone(string input)
    {
        Normalize(input).Should().Be(input);
    }

    [Fact]
    public void SingleCapitalLetter_IsTitleCased()
    {
        Normalize("X").Should().Be("X");
    }
}
