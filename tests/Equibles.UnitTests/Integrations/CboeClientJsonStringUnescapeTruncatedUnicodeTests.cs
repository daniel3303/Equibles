using System.Reflection;
using Equibles.Integrations.Cboe;

namespace Equibles.UnitTests.Integrations;

public class CboeClientJsonStringUnescapeTruncatedUnicodeTests
{
    // Contract: a \u escape that runs off the end of the input with fewer than four
    // following characters is malformed. The lenient unescaper must preserve it
    // verbatim and — critically — never throw: the four-hex slice is only taken once
    // there are four chars to take. A truncated CBOE JSON chunk must not crash the
    // parser with an out-of-range slice. Sibling pins cover valid \uXXXX and the
    // four-non-hex else branch; this exercises the length-guard FALSE arm.
    //
    // ReadOnlySpan<char> is a ref struct and can't be boxed for MethodInfo.Invoke,
    // so the private static method is bound via a typed delegate.
    private delegate string JsonStringUnescapeFn(ReadOnlySpan<char> input);

    [Fact]
    public void JsonStringUnescape_UnicodeEscapeTruncatedAtEnd_IsPreservedLiterallyWithoutThrowing()
    {
        var method = typeof(CboeClient).GetMethod(
            "JsonStringUnescape",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var unescape = (JsonStringUnescapeFn)
            Delegate.CreateDelegate(typeof(JsonStringUnescapeFn), method!);

        // Raw 4 chars: \ u 1 2 — only two of the required four hex digits are present.
        var result = unescape("\\u12".AsSpan());

        result.Should().Be("\\u12");
    }
}
