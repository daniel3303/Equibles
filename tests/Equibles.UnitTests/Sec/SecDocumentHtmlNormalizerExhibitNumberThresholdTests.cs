using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentHtmlNormalizerExhibitNumberThresholdTests
{
    private readonly SecDocumentHtmlNormalizer _sut = new();

    // Sibling to SecDocumentHtmlNormalizerExhibitDecimalSuffixTests (which pins
    // EX-10.1 as allowed). The exhibit branch's `exNumber < 100` guard rejects
    // SEC's XBRL technical exhibits (EX-101 = .xsd schema, EX-102 = label
    // linkbase, EX-103 = presentation linkbase, EX-104 = cover-page taxonomy)
    // — none of which are human-readable filing content. A refactor that
    // dropped the threshold would index every XBRL schema file as document
    // body, ballooning the persisted-text store with megabytes of taxonomy
    // markup and polluting the keyword index with us-gaap concept tags.
    [Fact]
    public void Normalize_ExhibitNumberGreaterThanOrEqualTo100_IsFiltered()
    {
        var sgml = """
            <DOCUMENT>
            <TYPE>EX-101
            <FILENAME>schema.htm
            <TEXT>
            <html><body><p>XBRL schema marker xyz</p></body></html>
            </TEXT>
            </DOCUMENT>
            """;

        var result = _sut.Normalize(sgml);

        result
            .Should()
            .NotContain(
                "XBRL schema marker xyz",
                "EX-101 (and higher) are XBRL technical exhibits and must be filtered out"
            );
    }
}
