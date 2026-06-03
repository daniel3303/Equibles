using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientTryParseMasterIndexLineEmptyAccessionTests
{
    [Fact]
    public void TryParseMasterIndexLine_FilenameYieldsNoAccession_ReturnsFalse()
    {
        // Contract (SecEdgarClient.cs:529-532): the accession number is derived
        // from the filename field; "if (string.IsNullOrEmpty(accession)) return
        // false". A 13F-HR row that is otherwise valid (numeric CIK, five fields)
        // but whose filename is empty produces no accession — it must be rejected,
        // never emitted as an entry with a blank AccessionNumber (which would build
        // a broken edgar/data URL downstream). Sibling pins cover short rows,
        // non-numeric CIK, non-13F form, and bad dates — none the empty-filename arm.
        var method = typeof(SecEdgarClient).GetMethod(
            "TryParseMasterIndexLine",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var line = "0000320193|Apple Inc.|13F-HR|2024-11-01|";
        object[] args = [line, new DateOnly(2024, 11, 1), null];

        var success = (bool)method!.Invoke(null, args);

        success.Should().BeFalse();
        args[2].Should().BeNull();
    }
}
