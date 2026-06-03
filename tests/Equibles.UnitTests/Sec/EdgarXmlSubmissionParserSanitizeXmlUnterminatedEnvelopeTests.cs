using Equibles.Sec.HostedService.Helpers;

namespace Equibles.UnitTests.Sec;

public class EdgarXmlSubmissionParserSanitizeXmlUnterminatedEnvelopeTests
{
    [Fact]
    public void SanitizeXml_OpeningEnvelopeWithoutClosingTag_DoesNotStripButStillEscapesAmpersand()
    {
        // Contract: the body is pulled out of <XML>...</XML> only when BOTH tags are present
        // (the strip guard is xmlStart >= 0 && xmlEnd > xmlStart). A truncated submission with
        // an opening <XML> but no closing </XML> can't be sliced, so the content is left intact
        // and only stray ampersands are escaped. Existing coverage exercises the full-envelope
        // and no-envelope cases; the open-but-unterminated branch is otherwise unexercised.
        var result = EdgarXmlSubmissionParser.SanitizeXml(
            "<XML><edgarSubmission>Smith & Co</edgarSubmission>"
        );

        result.Should().Be("<XML><edgarSubmission>Smith &amp; Co</edgarSubmission>");
    }
}
