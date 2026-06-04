using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Integrations.Sec;

public class SecEdgarClientIsMissingIndexErrorAccessDeniedTests
{
    // IsMissingIndexError returns true when the S3 403 body carries AccessDenied or NoSuchKey —
    // the non-trading-day signature that authorizes skipping the day. The existing test only pins
    // the negative (throttle page) case; both positive arms are unpinned. This pins the
    // AccessDenied arm so dropping it doesn't turn a quiet weekend skip into a paging error.
    [Fact]
    public void IsMissingIndexError_AccessDeniedBody_ReturnsTrue()
    {
        const string s3Body =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<Error><Code>AccessDenied</Code><Message>Access Denied</Message></Error>";

        var method = typeof(SecEdgarClient).GetMethod(
            "IsMissingIndexError",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (bool)method!.Invoke(null, [s3Body]);

        result.Should().BeTrue();
    }
}
