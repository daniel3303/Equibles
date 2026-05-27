using System.Reflection;
using Equibles.Errors.BusinessLogic;

namespace Equibles.UnitTests.Errors;

public class ErrorManagerTruncateNullTests
{
    [Fact]
    public void Truncate_NullValue_ReturnsNullWithoutThrowing()
    {
        // Truncate's guard (ErrorManager.cs:48): `value == null || value.Length
        // <= maxLength → return value`. The null arm is load-bearing because
        // Create's RequestSummary parameter defaults to null and is passed
        // through Truncate untouched by the upstream `??=` defaults applied to
        // Context and Message. Without this guard, `value.Length` would NRE.
        // The surrogate-pair sibling pins the cap-needed arm; this pins the
        // null arm. A refactor that "tightens" the guard to `value.Length <=
        // maxLength` (assuming callers will never pass null) would crash
        // every error save where the caller omitted RequestSummary.
        var method = typeof(ErrorManager).GetMethod(
            "Truncate",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, [null, 512]);

        result.Should().BeNull();
    }
}
