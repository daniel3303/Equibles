using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class HoldingsExportControllerSanitizePathTraversalTests
{
    // Sanitize wraps the URL-supplied CIK before it lands inside the
    // Content-Disposition filename of the institution-portfolio CSV download.
    // Its source comment commits to "strip anything that's unsafe in a filename
    // (slashes / quotes / control chars)"; the slash side is obvious, but the
    // dot side is non-obvious — '../' is the canonical path-traversal payload
    // and "." is also a header-significant character. A refactor that loosens
    // the allowlist to admit '.' would still keep slashes out yet readmit a
    // disguised traversal segment. Pin the dot+slash combo: only the
    // [A-Za-z0-9_-] allowlist may survive.
    [Fact]
    public void Sanitize_PathTraversalPayload_StripsAllSlashesAndDots()
    {
        var method = typeof(HoldingsExportController).GetMethod(
            "Sanitize",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method.Invoke(null, ["123/../456"]);

        result.Should().Be("123456");
    }
}
