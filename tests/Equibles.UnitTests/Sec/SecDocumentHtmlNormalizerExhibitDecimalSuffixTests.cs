using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentHtmlNormalizerExhibitDecimalSuffixTests
{
    private readonly SecDocumentHtmlNormalizer _sut = new();

    [Fact]
    public void Normalize_ExhibitTypeWithDecimalSubExhibitSuffix_IsAllowed()
    {
        // IsAllowedDocumentType's exhibit branch peels the decimal suffix via
        //   var exNumberPartClean = exNumberPart.Split('.')[0];
        // before int.TryParse. SEC exhibits routinely use the "EX-N.M" form
        // (e.g. EX-10.1 for material contracts as sub-exhibits of Form 10),
        // so the split is what lets the parser keep them. A "simplification"
        // that drops the Split — e.g. int.TryParse(exNumberPart, ...) directly —
        // would parse "10.1" as not-an-int, return false, and silently filter
        // out every decimal-suffixed exhibit, dropping a large fraction of the
        // attachments to material-contract filings. Pin EX-10.1 as allowed.
        var sgml = """
            <DOCUMENT>
            <TYPE>EX-10.1
            <FILENAME>contract.htm
            <TEXT>
            <html><body><p>Material contract body</p></body></html>
            </TEXT>
            </DOCUMENT>
            """;

        var result = _sut.Normalize(sgml);

        result.Should().Contain("Material contract body");
    }
}
