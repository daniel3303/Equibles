using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Contract: NormalizeCompanyName title-cases ALL-CAPS names, preserving only
/// real abbreviations (LLC, LP, …) and Roman numerals (I, II, III, IV, …) in
/// uppercase. The Roman numeral regex matches any string that decomposes into
/// valid Roman digits — including common English words like "MIX" (1009),
/// "DIV" (504), and "LIV" (54). These are not numerals in a company-name
/// context and should be title-cased normally.
/// </summary>
public class CompanySyncServiceNormalizeCompanyNameRomanNumeralFalsePositiveTests
{
    private static readonly MethodInfo NormalizeMethod = typeof(CompanySyncService).GetMethod(
        "NormalizeCompanyName",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    private static string Normalize(string name) => (string)NormalizeMethod.Invoke(null, [name]);

    [Fact]
    public void AllCapsName_MixIsEnglishWord_NotRomanNumeral1009()
    {
        var result = Normalize("QUICK MIX CORP");

        result.Should().Be("Quick Mix Corp");
    }
}
