using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class HoldingsExportControllerSanitizeAllStrippedFallbackTests
{
    // Sanitize has two fallback paths that both return "institution":
    //   (1) IsNullOrWhiteSpace(value) on the WAY IN (the early-return arm), and
    //   (2) IsNullOrEmpty(safe) AFTER the allowlist filter (when every char was
    //       stripped).
    // The sibling PathTraversal pin exercises a PARTIAL strip ("123/../456" →
    // "123456" — never hits a fallback). Arm (2) is unpinned: a refactor that
    // dropped the post-filter IsNullOrEmpty check on the assumption that
    // IsNullOrWhiteSpace upstream covers it would return "" for inputs that
    // contain only disallowed characters — producing a Content-Disposition
    // filename like `holdings-.csv`. Pin the all-stripped arm so any regression
    // that lets through the empty string fails here.
    [Fact]
    public void Sanitize_InputWithOnlyDisallowedCharacters_FallsBackToInstitution()
    {
        var method = typeof(HoldingsExportController).GetMethod(
            "Sanitize",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method.Invoke(null, ["!!!"]);

        result.Should().Be("institution");
    }
}
