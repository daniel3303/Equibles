using System.Reflection;
using Equibles.Integrations.Cboe;

namespace Equibles.UnitTests.Integrations;

public class CboeClientJsonStringUnescapeInvalidUnicodeTests
{
    // Contract: a \u escape whose four following characters are not valid hex is
    // malformed. The lenient unescaper must preserve it verbatim — emit the
    // literal "\u" plus the characters — rather than decode garbage, drop the
    // backslash, or throw. Sibling pins cover the valid \uXXXX and trailing-short
    // cases; this exercises the present-but-non-hex else branch on CBOE's
    // scraped optionsData blob.
    //
    // ReadOnlySpan<char> is a ref struct and can't be boxed for MethodInfo.Invoke,
    // so the private static method is bound via a typed delegate.
    private delegate string JsonStringUnescapeFn(ReadOnlySpan<char> input);

    [Fact]
    public void JsonStringUnescape_UnicodeEscapeWithNonHexDigits_IsPreservedLiterally()
    {
        var method = typeof(CboeClient).GetMethod(
            "JsonStringUnescape",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var unescape = (JsonStringUnescapeFn)
            Delegate.CreateDelegate(typeof(JsonStringUnescapeFn), method!);

        // Raw 6 chars: \ u Z Z Z Z — four post-\u chars are present but not hex.
        var result = unescape("\\uZZZZ".AsSpan());

        result.Should().Be("\\uZZZZ");
    }
}
