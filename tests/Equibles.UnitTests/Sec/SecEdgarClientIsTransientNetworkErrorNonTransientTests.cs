using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientIsTransientNetworkErrorNonTransientTests
{
    // IsTransientNetworkError classifies an HttpRequestException as retryable only when its inner
    // exception is Socket/IO/Authentication. An inner exception OUTSIDE that set must return false,
    // so a genuinely non-transient failure surfaces instead of looping the retry forever. The three
    // arm pins are all POSITIVE; only this catches a regression that broadens the `is` pattern.
    [Fact]
    public void IsTransientNetworkError_UnrelatedInnerException_ReturnsFalse()
    {
        var method = typeof(SecEdgarClient).GetMethod(
            "IsTransientNetworkError",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var wrapped = new HttpRequestException(
            "Permanent failure",
            new InvalidOperationException("not a transport error")
        );

        var result = (bool)method!.Invoke(null, [wrapped]);

        result.Should().BeFalse();
    }
}
