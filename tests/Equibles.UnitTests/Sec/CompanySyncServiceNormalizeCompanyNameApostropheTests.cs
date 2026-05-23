using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Contract: NormalizeCompanyName converts all-caps names to "Title Case."
/// TextInfo.ToTitleCase treats apostrophe as a word boundary, capitalising
/// the letter after it — producing "Mcdonald'S" instead of "Mcdonald's".
/// A possessive 's is not a new word; the capital S is incorrect title case.
/// </summary>
public class CompanySyncServiceNormalizeCompanyNameApostropheTests
{
    private static readonly MethodInfo NormalizeMethod = typeof(CompanySyncService).GetMethod(
        "NormalizeCompanyName",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    private static string Normalize(string name) => (string)NormalizeMethod.Invoke(null, [name]);

    [Fact]
    public void AllCapsNameWithApostrophe_DoesNotCapitaliseAfterApostrophe()
    {
        var result = Normalize("MCDONALD'S CORP");

        result.Should().Be("Mcdonald's Corp");
    }
}
