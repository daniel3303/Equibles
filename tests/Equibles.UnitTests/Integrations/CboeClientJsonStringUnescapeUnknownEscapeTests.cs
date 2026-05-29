using System.Reflection;
using Equibles.Integrations.Cboe;

namespace Equibles.UnitTests.Integrations;

public class CboeClientJsonStringUnescapeUnknownEscapeTests
{
    // Contract: an escape sequence the unescaper does not recognise (e.g. "\x")
    // is not a defined JSON escape. The lenient unescaper must preserve it
    // verbatim — keep the backslash and the following char — never silently drop
    // the backslash or throw. This is the switch default arm; sibling pins cover
    // the recognised escapes and the malformed-\u cases.
    //
    // ReadOnlySpan<char> is a ref struct and can't be boxed for MethodInfo.Invoke,
    // so the private static method is bound via a typed delegate.
    private delegate string JsonStringUnescapeFn(ReadOnlySpan<char> input);

    [Fact]
    public void JsonStringUnescape_UnrecognisedEscape_IsPreservedLiterally()
    {
        var method = typeof(CboeClient).GetMethod(
            "JsonStringUnescape",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var unescape = (JsonStringUnescapeFn)
            Delegate.CreateDelegate(typeof(JsonStringUnescapeFn), method!);

        // Raw 2 chars: \ x — backslash-x is not a defined JSON escape.
        var result = unescape("\\x".AsSpan());

        result.Should().Be("\\x");
    }
}
