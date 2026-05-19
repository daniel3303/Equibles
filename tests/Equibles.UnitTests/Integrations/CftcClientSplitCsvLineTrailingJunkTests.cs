using System.Reflection;
using Equibles.Integrations.Cftc;

namespace Equibles.UnitTests.Integrations;

public class CftcClientSplitCsvLineTrailingJunkTests
{
    private static readonly MethodInfo SplitCsvLineMethod = typeof(CftcClient).GetMethod(
        "SplitCsvLine",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    [Fact]
    public void SplitCsvLine_NonWhitespaceAfterClosingQuote_IsDiscardedFromValue()
    {
        // Contract (source comment, CftcClient.cs:251-252): "Characters after a
        // closing quote (CFTC's trailing padding before the next delimiter) are
        // likewise not part of the verbatim value." The existing pins cover
        // padding BEFORE the opening quote, embedded commas, and escaped
        // quotes — none exercises the post-close discard branch with non-
        // whitespace. Dropping the `!wasQuoted` guard in the unquoted-char
        // arm would silently start appending those chars to the field value
        // and corrupt every CFTC report row whose source padded with anything
        // other than whitespace. Pin the documented "verbatim from inside the
        // quotes" guarantee with a non-whitespace trailer so the regression
        // surfaces here.
        var line = "\"verbatim\"trailing,next";

        var fields = (string[])SplitCsvLineMethod.Invoke(null, [line])!;

        fields.Should().Equal("verbatim", "next");
    }
}
