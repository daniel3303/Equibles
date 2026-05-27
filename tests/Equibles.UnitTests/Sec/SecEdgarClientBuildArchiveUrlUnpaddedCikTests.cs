using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientBuildArchiveUrlUnpaddedCikTests
{
    // BuildArchiveUrl carries an explicit operational contract in its
    // inline comment: "Per-file URL uses unpadded CIK and the accession
    // number with dashes removed. Padded CIK works too but triggers a 301
    // redirect; skip the extra hop." Pin both: CIK stripped of leading
    // zeros AND accession dashes removed. A refactor that aligned this
    // helper with the sibling GetDocumentUrl (which uses FormatCik to
    // pad) would compile cleanly and still resolve via SEC's 301
    // redirect — but every artifact fetch would double its round-trip,
    // halving the SEC scraper's rate-limit budget at peak.
    [Fact]
    public void BuildArchiveUrl_PaddedCikAndDashedAccession_StripsZerosAndDashes()
    {
        var method = typeof(SecEdgarClient).GetMethod(
            "BuildArchiveUrl",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var url = (string)
            method.Invoke(null, ["0000320193", "0000320193-25-000001", "primary_doc.xml"]);

        url.Should().Contain("/data/320193/");
        url.Should().Contain("/000032019325000001/");
        url.Should().NotContain("/0000320193/");
        url.Should().NotContain("0000320193-25-000001");
    }
}
