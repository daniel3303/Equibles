using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentEnvelopeParserContainsInlineXbrlElementTests
{
    // Contract (doc-comment): true when the document carries inline XBRL — the ix namespace
    // declaration OR any element in that namespace. This pins the second, independent trigger:
    // content with an `<ix:` element but NO `xmlns:ix=` declaration must still be detected — a
    // naive check of only the namespace-declaration form would miss element-only fragments.
    [Fact]
    public void ContainsInlineXbrl_IxElementWithoutNamespaceDeclaration_ReturnsTrue()
    {
        var content = "<ix:nonFraction contextRef=\"c1\">1234</ix:nonFraction>";

        SecDocumentEnvelopeParser.ContainsInlineXbrl(content).Should().BeTrue();
    }
}
