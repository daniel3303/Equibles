using System.Reflection;
using System.Security.Authentication;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientIsTransientNetworkErrorTlsTests
{
    // Sibling to SecEdgarClientIsTransientNetworkErrorSocketTests. That pin
    // covers the SocketException arm (DNS resolution failures). This pin
    // covers the structurally distinct AuthenticationException arm — the TLS
    // handshake failure that the source comment explicitly names alongside
    // DNS as a transient connectivity problem.
    //
    // The OR-chain in IsTransientNetworkError treats three exception types
    // as transient: SocketException (DNS/sockets), IOException (network
    // reads/writes), AuthenticationException (TLS handshake). Each is a
    // distinct production failure mode:
    //   • SocketException: pinned by the sibling.
    //   • IOException: covers mid-stream resets, broken pipes, connection
    //     drops during body read.
    //   • AuthenticationException: covers TLS handshake failures — SEC's
    //     load balancer rotates TLS certificates without notice, and a
    //     stale CA chain on the worker host (or a transient mismatch
    //     during a rolling certificate update) surfaces as
    //     AuthenticationException, not SocketException.
    //
    // The risk this pin uniquely catches and that the Socket sibling cannot:
    //   A refactor that narrows the inner-exception type set — e.g.,
    //   "consolidate transient-network classification under SocketException
    //   and IOException only, since they cover the actual network layer"
    //   under the (false) intuition that TLS errors are auth-config issues
    //   rather than transient connectivity — would compile, pass the
    //   Socket sibling pin (SocketException still classified as transient),
    //   and silently demote every TLS-handshake-flake to a dashboard-visible
    //   error. SEC's EDGAR CDN does roll certificates; during the
    //   modernization cutover (2024-2025) operators saw bursts of TLS
    //   handshake failures that the production retry loop absorbed
    //   silently — without this arm, every burst would surface as a
    //   user-visible incident.
    //
    // The complementary risk: a refactor that swapped the arms' return
    // values (e.g. accidentally inverting the OR with !is) would compile
    // and flip every transient classification to "real defect" — caught
    // by BOTH the Socket sibling and this pin, both arms now defended.
    //
    // Pin: HttpRequestException wrapping an AuthenticationException →
    // IsTransientNetworkError returns true. A drop of the
    // AuthenticationException arm surfaces here.
    [Fact]
    public void IsTransientNetworkError_AuthenticationExceptionInner_ReturnsTrue()
    {
        var method = typeof(SecEdgarClient).GetMethod(
            "IsTransientNetworkError",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var wrapped = new HttpRequestException(
            "TLS handshake failed",
            new AuthenticationException("SSL connection could not be established")
        );

        var result = (bool)method!.Invoke(null, [wrapped]);

        result.Should().BeTrue();
    }
}
