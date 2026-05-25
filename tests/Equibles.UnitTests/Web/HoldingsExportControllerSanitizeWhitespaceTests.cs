using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class HoldingsExportControllerSanitizeWhitespaceTests
{
    private static readonly MethodInfo SanitizeMethod = typeof(HoldingsExportController).GetMethod(
        "Sanitize",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    [Fact]
    public void Sanitize_WhitespaceOnlyInput_ReturnsFallback()
    {
        // Contract: CIK values become part of the Content-Disposition filename.
        // Whitespace-only input must produce a safe fallback, not an empty or
        // whitespace filename that corrupts the header.
        var result = (string)SanitizeMethod.Invoke(null, ["   "]);

        result.Should().Be("institution");
    }
}
