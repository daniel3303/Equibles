using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Contract (from <see cref="CompanySyncServiceNormalizeCompanyNameTests"/>):
/// real Roman numerals stay uppercase. XL = 40 is a real Roman numeral and
/// satisfies the regex pattern just like XV (15) or XX (20), so a SEC fund
/// name like "CAPITAL FUND XL LP" must preserve the XL token.
/// </summary>
public class CompanySyncServiceNormalizeCompanyNameRomanNumeralFalseNegativeTests
{
    private static readonly MethodInfo NormalizeMethod = typeof(CompanySyncService).GetMethod(
        "NormalizeCompanyName",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    private static string Normalize(string name) => (string)NormalizeMethod.Invoke(null, [name]);

    [Fact]
    public void AllCapsName_XlIsRomanNumeral40_StayUpperCase()
    {
        var result = Normalize("CAPITAL FUND XL LP");

        result.Should().Be("Capital Fund XL LP");
    }
}
