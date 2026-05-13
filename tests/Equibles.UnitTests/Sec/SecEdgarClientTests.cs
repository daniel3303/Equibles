using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Tests for <see cref="SecEdgarClient"/>. The public surface drives real HTTP calls
/// against SEC EDGAR; we exercise the pure-logic private static URL helpers via
/// reflection — same pattern as YahooFinanceClientTests and CftcClientTests.
/// </summary>
public class SecEdgarClientTests {
    private static readonly MethodInfo FormatCikMethod = typeof(SecEdgarClient)
        .GetMethod("FormatCik", BindingFlags.NonPublic | BindingFlags.Static);

    [Fact]
    public void FormatCik_ShortCik_PadsLeftToTenDigitsWithZeros() {
        // SEC EDGAR's archive URLs require the CIK in a specific 10-digit
        // zero-padded form: `/Archives/edgar/data/{0000320193}/...` for
        // Apple's real CIK `320193`. The unpadded form (`320193`) returns
        // 404 from the CDN — SEC's load balancer rejects bare CIKs even
        // when they're numerically valid.
        //
        // The risk this pins: a refactor that swaps `PadLeft(10, '0')` for
        // any of the plausible "tidy-up" alternatives — `PadLeft(8, '0')`
        // (off-by-two from the 8-digit accession-number prefix elsewhere in
        // the file), `PadLeft(10, ' ')` (default char is space, easy to
        // miss when reviewing), or dropping the pad entirely under the
        // assumption that SEC accepts unpadded inputs — would compile
        // cleanly and pass every existing integration test whose fixture
        // CIK happens to already be 10 digits OR which uses HTTP-level
        // mocks that don't care about the URL format. Every real
        // SEC fetch against a sub-10-digit CIK (the majority — only the
        // largest registrants have CIKs ≥ 1_000_000_000) would silently
        // 404, the worker would log the error and continue, and the
        // filing pipeline would silently stall for those companies with
        // no CI signal.
        //
        // Pin the most realistic case: Apple's CIK `320193` padded to
        // `0000320193`. Asserting BOTH the digit count AND the leading
        // zeros distinguishes this from a `PadLeft(10, ' ')` regression
        // (which would yield "    320193") and a `PadLeft(6)` regression
        // (which would yield "320193").
        var result = (string)FormatCikMethod.Invoke(null, ["320193"]);

        result.Should().Be("0000320193");
    }
}
