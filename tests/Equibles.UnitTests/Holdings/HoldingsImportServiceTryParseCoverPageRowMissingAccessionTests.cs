using System.Reflection;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceTryParseCoverPageRowMissingAccessionTests
{
    [Fact]
    public void TryParseCoverPageRow_RowMissingAccessionField_ReturnsFalseWithoutDictionaryLookup()
    {
        // Sibling to TryParseCoverPageRowOrphanAccessionTests. The
        // existing pin defends the `!submissions.ContainsKey(accession)`
        // arm — accession is known to GetValue but unknown to submissions.
        // This sibling defends the OR's FIRST arm: `IsNullOrEmpty(accession)
        // → false`. GetValue returns null when the field is absent from
        // the row dictionary. A refactor that drops the IsNullOrEmpty
        // guard ("ContainsKey already handles missing keys") would
        // short-circuit to `submissions.ContainsKey(null)` — Dictionary
        // throws ArgumentNullException on a null key. That would crash
        // the whole cover-page parse pass on the first row missing
        // ACCESSION_NUMBER, instead of the contractual graceful skip.
        var method = typeof(HoldingsImportService).GetMethod(
            "TryParseCoverPageRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        // Row has NO ACCESSION_NUMBER field — GetValue returns null.
        var row = new Dictionary<string, string> { ["FILINGMANAGER_NAME"] = "Some Fund" };
        var submissions = new Dictionary<string, SubmissionRow>();
        var args = new object[] { row, submissions, null };

        var resolved = (bool)method!.Invoke(null, args);

        resolved.Should().BeFalse();
        args[2].Should().BeNull();
    }
}
