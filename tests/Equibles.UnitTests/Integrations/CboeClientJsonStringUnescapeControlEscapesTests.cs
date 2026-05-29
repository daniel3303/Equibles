using System.Reflection;
using Equibles.Integrations.Cboe;

namespace Equibles.UnitTests.Integrations;

public class CboeClientJsonStringUnescapeControlEscapesTests
{
    // Contract (JSON spec, RFC 8259 §7): the two-character escapes \n \t \r \b \f
    // decode to their control characters U+000A, U+0009, U+000D, U+0008, U+000C.
    // The sibling pins cover the escaped-backslash and trailing-\u boundary; the
    // named control-escape arms decode CBOE's embedded optionsData blob and are
    // otherwise unexercised. A mapping bug (e.g. \t emitting 'r' or 'b' swapped
    // with 'f') would silently corrupt the scraped payload.
    //
    // ReadOnlySpan<char> is a ref struct and can't be boxed for MethodInfo.Invoke,
    // so the private static method is bound via a typed delegate.
    private delegate string JsonStringUnescapeFn(ReadOnlySpan<char> input);

    [Fact]
    public void JsonStringUnescape_NamedControlEscapes_DecodeToTheirControlCharacters()
    {
        var method = typeof(CboeClient).GetMethod(
            "JsonStringUnescape",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var unescape = (JsonStringUnescapeFn)
            Delegate.CreateDelegate(typeof(JsonStringUnescapeFn), method!);

        // Raw JSON body: a \n b \t c \r d \b e \f f
        var result = unescape("a\\nb\\tc\\rd\\be\\ff".AsSpan());

        result.Should().Be("a\nb\tc\rd\be\ff");
    }
}
