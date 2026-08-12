using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceDeduplicateNewHoldingsAmendmentTests
{
    // Contract: a "NEW HOLDINGS" amendment only ADDS positions, so it supersedes
    // nothing — the dedup must keep the original (or the newest restatement)
    // as the base plus every additive amendment after it. Keeping only the
    // latest submission per (CIK, period) discarded the base's entire book
    // from every bulk import: all three restructured Vanguard entities filed a
    // NEW HOLDINGS amendment for 2026-03-31, so their 3,600-row originals were
    // skipped on every pass and the re-import lever could never heal them
    // (EquiblesCommercial#7163 — 48 filers in that one data set alone).
    //
    // A RESTATEMENT (or an untyped legacy amendment — the null-cover-page arm
    // is pinned by the sibling FilingDatePrimary/Tiebreaker tests) still
    // supersedes everything filed before it: HandleAmendments deletes the
    // holder-quarter for those, so re-streaming a superseded original would
    // resurrect the positions the restatement removed.

    private static SubmissionRow Submission(
        string accession,
        string filingDate,
        string formType = "13F-HR"
    ) =>
        new()
        {
            AccessionNumber = accession,
            Cik = "1811242",
            PeriodOfReport = "2026-03-31",
            FilingDate = filingDate,
            FormType = formType,
        };

    private static CoverPageRow Amendment(string accession, string amendmentType) =>
        new()
        {
            AccessionNumber = accession,
            IsAmendment = "Y",
            AmendmentType = amendmentType,
        };

    private static ImportContext Context(
        IEnumerable<SubmissionRow> submissions,
        IEnumerable<CoverPageRow> coverPages
    )
    {
        return new ImportContext
        {
            Submissions = submissions.ToDictionary(
                s => s.AccessionNumber,
                StringComparer.OrdinalIgnoreCase
            ),
            CoverPages = coverPages.ToDictionary(
                c => c.AccessionNumber,
                StringComparer.OrdinalIgnoreCase
            ),
        };
    }

    [Fact]
    public void DeduplicateSubmissions_NewHoldingsAmendment_KeepsTheOriginalBeneathIt()
    {
        var context = Context(
            [
                Submission("0001811242-26-000004", "08-MAY-2026"),
                Submission("0001811242-26-000008", "15-MAY-2026", "13F-HR/A"),
            ],
            [Amendment("0001811242-26-000008", "NEW HOLDINGS")]
        );

        HoldingsImportService.DeduplicateSubmissions(context);

        context
            .Submissions.Keys.Should()
            .BeEquivalentTo("0001811242-26-000004", "0001811242-26-000008");
    }

    [Fact]
    public void DeduplicateSubmissions_RestatementAmendment_StillSupersedesTheOriginal()
    {
        var context = Context(
            [
                Submission("0001811242-26-000004", "08-MAY-2026"),
                Submission("0001811242-26-000008", "15-MAY-2026", "13F-HR/A"),
            ],
            [Amendment("0001811242-26-000008", "RESTATEMENT")]
        );

        HoldingsImportService.DeduplicateSubmissions(context);

        context.Submissions.Keys.Should().BeEquivalentTo("0001811242-26-000008");
    }

    [Fact]
    public void DeduplicateSubmissions_RestatementAfterNewHoldings_SupersedesBoth()
    {
        var context = Context(
            [
                Submission("0001811242-26-000004", "08-MAY-2026"),
                Submission("0001811242-26-000008", "15-MAY-2026", "13F-HR/A"),
                Submission("0001811242-26-000012", "22-MAY-2026", "13F-HR/A"),
            ],
            [
                Amendment("0001811242-26-000008", "NEW HOLDINGS"),
                Amendment("0001811242-26-000012", "RESTATEMENT"),
            ]
        );

        HoldingsImportService.DeduplicateSubmissions(context);

        context.Submissions.Keys.Should().BeEquivalentTo("0001811242-26-000012");
    }

    [Fact]
    public void DeduplicateSubmissions_NewHoldingsAfterRestatement_KeepsRestatementAndAddition()
    {
        var context = Context(
            [
                Submission("0001811242-26-000004", "08-MAY-2026"),
                Submission("0001811242-26-000008", "15-MAY-2026", "13F-HR/A"),
                Submission("0001811242-26-000012", "22-MAY-2026", "13F-HR/A"),
            ],
            [
                Amendment("0001811242-26-000008", "RESTATEMENT"),
                Amendment("0001811242-26-000012", "NEW HOLDINGS"),
            ]
        );

        HoldingsImportService.DeduplicateSubmissions(context);

        context
            .Submissions.Keys.Should()
            .BeEquivalentTo("0001811242-26-000008", "0001811242-26-000012");
    }

    [Fact]
    public void DeduplicateSubmissions_SeveralNewHoldingsAmendments_AllSurviveWithTheOriginal()
    {
        var context = Context(
            [
                Submission("0001811242-26-000004", "08-MAY-2026"),
                Submission("0001811242-26-000008", "15-MAY-2026", "13F-HR/A"),
                Submission("0001811242-26-000012", "22-MAY-2026", "13F-HR/A"),
            ],
            [
                Amendment("0001811242-26-000008", "NEW HOLDINGS"),
                Amendment("0001811242-26-000012", "NEW HOLDINGS"),
            ]
        );

        HoldingsImportService.DeduplicateSubmissions(context);

        context.Submissions.Should().HaveCount(3);
    }

    // A data set can hold ONLY additive amendments for a (CIK, period) — the
    // original lives in an earlier quarter's archive. Nothing supersedes
    // anything; both amendments stream.
    [Fact]
    public void DeduplicateSubmissions_OnlyNewHoldingsAmendments_KeepsAll()
    {
        var context = Context(
            [
                Submission("0001811242-26-000008", "15-MAY-2026", "13F-HR/A"),
                Submission("0001811242-26-000012", "22-MAY-2026", "13F-HR/A"),
            ],
            [
                Amendment("0001811242-26-000008", "NEW HOLDINGS"),
                Amendment("0001811242-26-000012", "NEW HOLDINGS"),
            ]
        );

        HoldingsImportService.DeduplicateSubmissions(context);

        context.Submissions.Should().HaveCount(2);
    }
}
