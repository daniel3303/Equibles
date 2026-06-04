using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Integrations.Sec;

public class SecEdgarClientIsMissingIndexErrorNoSuchKeyTests
{
    // IsMissingIndexError returns true for the S3 "missing object" signatures
    // AccessDenied OR NoSuchKey. The AccessDenied arm is pinned; this pins the
    // NoSuchKey arm of the OR (a partial branch), so dropping it wouldn't turn a
    // NoSuchKey non-trading-day response into a paging/retry error.
    [Fact]
    public void IsMissingIndexError_NoSuchKeyBody_ReturnsTrue()
    {
        const string s3Body =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<Error><Code>NoSuchKey</Code><Message>The specified key does not exist.</Message></Error>";

        var method = typeof(SecEdgarClient).GetMethod(
            "IsMissingIndexError",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (bool)method!.Invoke(null, [s3Body]);

        result.Should().BeTrue();
    }
}
