using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecSgmlEnvelopeTagValueTrailerTests
{
    // Contract (documented on TryGetTagValue, SecSgmlEnvelope.cs:9-11): the value
    // "ends at the first line break or '<', is trimmed, and only its first
    // whitespace-delimited token is returned — SEC sometimes appends a descriptive
    // trailer after the bare value." SEC full-submission text is CRLF-terminated,
    // so the bare token must survive a multi-space trailer AND a trailing \r\n:
    // "<TYPE>10-K   Annual Report\r\n" must yield exactly "10-K".
    [Fact]
    public void TryGetTagValue_ValueHasDescriptiveTrailerAndCrlf_ReturnsBareFirstToken()
    {
        var block = "<TYPE>10-K   Annual Report\r\n<FILENAME>d12345.htm\r\n";

        var found = SecSgmlEnvelope.TryGetTagValue(block, "TYPE", out var value);

        found.Should().BeTrue();
        value.Should().Be("10-K");
    }
}
