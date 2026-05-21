using System.Reflection;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceTryParseCoverPageRowOrphanAccessionTests
{
    [Fact]
    public void TryParseCoverPageRow_AccessionNotInSubmissions_ReturnsFalseDroppingOrphan()
    {
        // TryParseCoverPageRow (#1531) is the per-row gate for the 13F
        // COVERPAGE.tsv stream. The helper imposes a referential-integrity
        // check: the cover-page row's ACCESSION_NUMBER must match a
        // submission row already parsed in this same import. Cover pages
        // arrive AFTER submissions in the bulk-dataset ZIP, and the
        // submission pass already filtered to 13F-HR / 13F-HR/A — so a
        // cover page whose accession is not in `submissions` is an
        // orphan (its submission was either non-13F, before the
        // configured cutoff, or empty-accession) and must be dropped.
        //
        // The risk this catches: a refactor that drops the
        //   `!submissions.ContainsKey(accession)`
        // arm — perhaps under the false intuition that "cover pages
        // and submissions can be processed independently" or that the
        // downstream join handles it — would compile, pass any test
        // where every cover page has a matching submission (which is
        // every existing fixture), and admit orphan cover pages into
        // `context.CoverPages`. The HandleAmendments path then keys
        // by accession to look up the submission's CIK and report
        // date; a missing submission produces a KeyNotFoundException
        // mid-import, aborting the whole data set.
        //
        // Pin: feed a cover-page row whose ACCESSION_NUMBER references
        // an accession not present in the submissions dictionary.
        // Helper must return false; out coverPage must be null.
        var method = typeof(HoldingsImportService).GetMethod(
            "TryParseCoverPageRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ACCESSION_NUMBER"] = "0001067983-26-999999",
            ["ISAMENDMENT"] = "N",
            ["FILINGMANAGER_NAME"] = "Orphan LLC",
        };
        var submissions = new Dictionary<string, SubmissionRow>
        {
            ["0001067983-26-000300"] = new SubmissionRow
            {
                AccessionNumber = "0001067983-26-000300",
            },
        };

        object[] args = [row, submissions, null];
        var success = (bool)method.Invoke(null, args);

        success.Should().BeFalse();
        args[2].Should().BeNull();
    }
}
