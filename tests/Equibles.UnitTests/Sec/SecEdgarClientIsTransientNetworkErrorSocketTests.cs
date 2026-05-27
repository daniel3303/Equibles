using System.Net.Sockets;
using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientIsTransientNetworkErrorSocketTests
{
    // IsTransientNetworkError's WHY-comment names DNS resolution, socket and
    // TLS handshake failures as the transient set — "retried, never recorded
    // as dashboard errors". DNS lookup failures surface from HttpClient as
    // an HttpRequestException wrapping a SocketException. A refactor that
    // narrowed the inner-exception set (e.g. removed SocketException because
    // "we already handle Socket-related stuff elsewhere") would silently
    // demote every transient DNS hiccup to a dashboard-visible error and
    // bury real defects in operational noise. Pin the SocketException arm.
    [Fact]
    public void IsTransientNetworkError_SocketExceptionInner_ReturnsTrue()
    {
        var method = typeof(SecEdgarClient).GetMethod(
            "IsTransientNetworkError",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var wrapped = new HttpRequestException(
            "DNS failure",
            new SocketException((int)SocketError.HostNotFound)
        );

        var result = (bool)method.Invoke(null, [wrapped]);

        result.Should().BeTrue();
    }
}
