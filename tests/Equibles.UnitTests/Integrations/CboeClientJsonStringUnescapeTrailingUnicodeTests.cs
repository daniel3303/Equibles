using System.Reflection;
using Equibles.Integrations.Cboe;

namespace Equibles.UnitTests.Integrations;

public class CboeClientJsonStringUnescapeTrailingUnicodeTests
{
    // CboeClient.JsonStringUnescape is a private, untested, hand-rolled JSON
    // string unescaper used to decode the embedded `optionsData` blob scraped
    // from cdn.cboe.com's daily put/call page. Hand-rolled escape parsers are a
    // classic boundary-bug surface; this one's most fragile line is the
    // `\uXXXX` branch guard:
    //
    //     case 'u' when i + 4 < input.Length:
    //         var hex = input.Slice(i + 1, 4);   // i points AT the 'u'
    //
    // The four hex digits sit at i+1..i+4, so the last digit's valid index is
    // input.Length-1, i.e. the guard must admit `i + 4 == input.Length - 1`.
    // The contract: a `\uXXXX` escape whose four hex digits are the FINAL four
    // characters of the input must still be decoded — not dropped or garbled.
    //
    // A "safer-looking" tightening to `i + 5 < input.Length` (the plausible
    // off-by-one) would reject exactly this exact-fit case, silently passing
    // the raw `é` through instead of decoding it, corrupting any CBOE
    // product label/field ending in a non-ASCII character.
    //
    // ReadOnlySpan<char> is a ref struct and can't be boxed into the object[]
    // that MethodInfo.Invoke requires, so the private static method is bound
    // via a typed delegate instead.
    private delegate string JsonStringUnescapeFn(ReadOnlySpan<char> input);

    [Fact]
    public void JsonStringUnescape_TrailingFourHexUnicodeEscape_DecodesFinalEscape()
    {
        var method = typeof(CboeClient).GetMethod(
            "JsonStringUnescape",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var unescape = (JsonStringUnescapeFn)
            Delegate.CreateDelegate(typeof(JsonStringUnescapeFn), method!);

        // Input span is the raw 9 chars: c a f \ u 0 0 e 9 — the "00e9" hex
        // digits are the last four characters, exercising the guard's exact fit.
        var result = unescape("caf\\u00e9".AsSpan());

        result.Should().Be("café"); // "café" — the U+00E9 escape decoded
    }
}
