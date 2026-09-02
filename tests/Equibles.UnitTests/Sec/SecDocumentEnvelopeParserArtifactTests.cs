using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentEnvelopeParserArtifactTests
{
    [Fact]
    public void EnumerateArtifacts_MultipleDebtExhibits_PreservesIndividualMetadataAndBodies()
    {
        var envelope = """
            <SEC-DOCUMENT>
            <DOCUMENT>
            <TYPE>8-K
            <SEQUENCE>1
            <FILENAME>issuer-8k.htm
            <DESCRIPTION>Current report
            <TEXT><html><body>Item 8.01</body></html></TEXT>
            </DOCUMENT>
            <DOCUMENT>
            <TYPE>EX-4.1
            <SEQUENCE>2
            <FILENAME>indenture.htm
            <DESCRIPTION>Supplemental Indenture
            <TEXT><html><body>2.00% Notes due 2030</body></html></TEXT>
            </DOCUMENT>
            <DOCUMENT>
            <TYPE>EX-10.1
            <SEQUENCE>3
            <FILENAME>credit-agreement.htm
            <DESCRIPTION>Credit Agreement
            <TEXT><html><body>Revolving Commitments</body></html></TEXT>
            </DOCUMENT>
            </SEC-DOCUMENT>
            """;

        var artifacts = SecDocumentEnvelopeParser.EnumerateArtifacts(envelope, "issuer-8k.htm");

        artifacts.Should().HaveCount(3);
        artifacts[0].IsPrimary.Should().BeTrue();
        artifacts[1].Type.Should().Be("EX-4.1");
        artifacts[1].SequenceNumber.Should().Be(2);
        artifacts[1].Description.Should().Be("Supplemental Indenture");
        artifacts[1].Body.Should().Contain("2.00% Notes due 2030");
        artifacts[2].FileName.Should().Be("credit-agreement.htm");
    }

    [Fact]
    public void EnumerateArtifacts_MultiWordType_PreservesFullSourceValue()
    {
        var envelope = """
            <DOCUMENT>
            <TYPE>DEF 14A
            <SEQUENCE>1
            <FILENAME>proxy.htm
            <TEXT>proxy statement</TEXT>
            </DOCUMENT>
            """;

        var artifact = SecDocumentEnvelopeParser.EnumerateArtifacts(envelope).Single();

        artifact.Type.Should().Be("DEF 14A");
    }

    [Fact]
    public void EnumerateArtifacts_UnsafeFilename_RefusesArtifact()
    {
        var envelope = """
            <DOCUMENT>
            <TYPE>EX-10.1
            <SEQUENCE>1
            <FILENAME>%2e%2e%2fsecret.htm
            <TEXT>secret</TEXT>
            </DOCUMENT>
            """;

        SecDocumentEnvelopeParser.EnumerateArtifacts(envelope).Should().BeEmpty();
    }

    [Fact]
    public void EnumerateArtifacts_UnknownPrimary_FallsBackToFirstNamedBlock()
    {
        var envelope = """
            <DOCUMENT>
            <TYPE>10-Q
            <SEQUENCE>1
            <FILENAME>issuer-10q.htm
            <TEXT>quarterly report</TEXT>
            </DOCUMENT>
            """;

        var artifact = SecDocumentEnvelopeParser.EnumerateArtifacts(envelope).Single();

        artifact.IsPrimary.Should().BeTrue();
    }
}
