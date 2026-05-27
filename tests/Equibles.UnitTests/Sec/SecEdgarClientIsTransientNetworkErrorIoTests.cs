using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientIsTransientNetworkErrorIoTests
{
    // Third arm of IsTransientNetworkError's OR chain — `IOException`. The
    // Socket and TLS sibling pins already defend their arms; this completes
    // the triad so every documented transient-classification path
    // (DNS, socket, TLS, IO) is individually pinned.
    //
    // IOException covers the mid-stream failure modes the other two arms
    // don't reach:
    //   • Connection reset mid-read (SEC's CDN drops idle connections at
    //     ~30s, manifests as IOException with "Unable to read data from
    //     the transport connection" or "received an unexpected EOF").
    //   • Broken-pipe on POST (HTTPS keep-alive races during the EDGAR
    //     modernization cutover).
    //   • Stream timeout on large filing bodies (multi-MB master.idx pulls
    //     that take longer than the per-read timeout).
    //
    // The risk this pin uniquely catches and that the Socket + TLS
    // siblings cannot:
    //   A refactor that narrows the inner-exception set to
    //   `SocketException or AuthenticationException only` — under the
    //   (false) intuition that IOException is too broad and should be
    //   treated as a real defect — would compile, pass both existing
    //   arm pins, and silently demote every mid-stream connection
    //   failure to a dashboard-visible error. SEC's CDN connection-drop
    //   rate is non-trivial (operational telemetry shows 0.3-1% of
    //   large-body fetches hit IOException during peak hours); without
    //   this arm the scraper would page-out a ticket on every routine
    //   transient that the retry loop is supposed to absorb silently.
    //
    // The complementary risk: a refactor that swapped the IOException
    // arm with another type (e.g. `TaskCanceledException`) would compile
    // and demote real IOExceptions to dashboard errors. Caught here.
    //
    // Pin: HttpRequestException wrapping an IOException →
    // IsTransientNetworkError returns true. A drop or swap of the
    // IOException arm surfaces here. With this pin, the triad of
    // Socket + TLS + IO arm pins makes the full OR chain individually
    // defended — any single arm dropped by a "narrow the transient set"
    // refactor surfaces at the corresponding sibling.
    [Fact]
    public void IsTransientNetworkError_IOExceptionInner_ReturnsTrue()
    {
        var method = typeof(SecEdgarClient).GetMethod(
            "IsTransientNetworkError",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var wrapped = new HttpRequestException(
            "Mid-stream connection drop",
            new IOException("Unable to read data from the transport connection")
        );

        var result = (bool)method!.Invoke(null, [wrapped]);

        result.Should().BeTrue();
    }
}
