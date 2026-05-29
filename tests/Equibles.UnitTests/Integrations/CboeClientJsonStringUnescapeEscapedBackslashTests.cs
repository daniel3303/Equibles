using System.Reflection;
using Equibles.Integrations.Cboe;

namespace Equibles.UnitTests.Integrations;

public class CboeClientJsonStringUnescapeEscapedBackslashTests
{
    // Contract (JSON spec, RFC 8259): an escaped backslash `\\` decodes to a
    // single literal `\`, and that backslash must NOT then combine with a
    // following `u` to form a unicode escape. So `\\u0041` is backslash +
    // the literal text "u0041" — six characters — never the letter "A".
    // This is the classic unescaper state-machine trap: a parser that scans
    // for the substring `\u` instead of consuming `\\` as one unit would
    // wrongly emit "A". The escaped-backslash arm and its literal-passthrough
    // are otherwise unexercised by the existing trailing-unicode pin.
    //
    // ReadOnlySpan<char> is a ref struct and can't be boxed into the object[]
    // MethodInfo.Invoke needs, so bind the private static method via a delegate.
    private delegate string JsonStringUnescapeFn(ReadOnlySpan<char> input);

    [Fact]
    public void JsonStringUnescape_EscapedBackslashBeforeU_DoesNotFormUnicodeEscape()
    {
        var method = typeof(CboeClient).GetMethod(
            "JsonStringUnescape",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var unescape = (JsonStringUnescapeFn)
            Delegate.CreateDelegate(typeof(JsonStringUnescapeFn), method!);

        // Raw 7 chars: \ \ u 0 0 4 1 — first pair is an escaped backslash.
        var result = unescape("\\\\u0041".AsSpan());

        result.Should().Be("\\u0041"); // literal backslash + "u0041", not "A"
    }
}
