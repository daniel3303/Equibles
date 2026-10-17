using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// A holder's 13F file number, CRD and SEC file number follow its newest 13F cover page, so a filer
/// first seen before CRD and SEC file numbers joined the form (2023) is not stuck with blanks.
/// </summary>
public class HoldingsImportServiceRefreshHolderIdentityNumbersTests
{
    private const string Cik = "102909";

    [Fact]
    public void NewestReport_SetsTheNumbersAndStampsItsAccession()
    {
        var context = Context(
            Filing("0000102909-25-000001", "14-MAY-2025", "31-MAR-2025", crd: null),
            Filing("0000102909-25-000002", "14-AUG-2025", "30-JUN-2025", crd: "000105958")
        );
        var holder = new InstitutionalHolder { Cik = Cik, CrdNumber = null };

        HoldingsImportService.RefreshHolderIdentityNumbers(context, [holder], NoStoredReports);

        holder.CrdNumber.Should().Be("000105958");
        holder.Form13FFileNumber.Should().Be("028-01234");
        holder.SecFileNumber.Should().Be("801-11953");
        holder.IdentityAccession.Should().Be("0000102909-25-000002");
    }

    [Fact]
    public void BlankNumberOnTheNewestCover_KeepsTheKnownOne()
    {
        var context = Context(
            Filing("0000102909-25-000002", "14-AUG-2025", "30-JUN-2025", crd: null)
        );
        var holder = new InstitutionalHolder { Cik = Cik, CrdNumber = "000105958" };

        HoldingsImportService.RefreshHolderIdentityNumbers(context, [holder], NoStoredReports);

        holder.CrdNumber.Should().Be("000105958");
        holder.IdentityAccession.Should().Be("0000102909-25-000002");
    }

    [Fact]
    public void StoredReportForALaterQuarter_IsNotOverriddenByAnOlderImport()
    {
        var context = Context(
            Filing("0000102909-24-000001", "14-MAY-2024", "31-MAR-2024", crd: "000999999")
        );
        var holder = new InstitutionalHolder
        {
            Cik = Cik,
            CrdNumber = "000105958",
            IdentityAccession = "0000102909-25-000002",
        };

        HoldingsImportService.RefreshHolderIdentityNumbers(
            context,
            [holder],
            new Dictionary<Guid, DateOnly> { [holder.Id] = new DateOnly(2025, 6, 30) }
        );

        holder.CrdNumber.Should().Be("000105958");
        holder.IdentityAccession.Should().Be("0000102909-25-000002");
    }

    [Fact]
    public void NewHoldingsAmendment_DoesNotSpeakForTheFilersIdentity()
    {
        var context = Context(
            Filing("0000102909-25-000002", "14-AUG-2025", "30-JUN-2025", crd: "000105958"),
            Filing(
                "0000102909-25-000003",
                "20-AUG-2025",
                "30-JUN-2025",
                crd: "000999999",
                amendmentType: "NEW HOLDINGS"
            )
        );
        var holder = new InstitutionalHolder { Cik = Cik };

        HoldingsImportService.RefreshHolderIdentityNumbers(context, [holder], NoStoredReports);

        holder.CrdNumber.Should().Be("000105958");
        holder.IdentityAccession.Should().Be("0000102909-25-000002");
    }

    [Fact]
    public void ScheduleImport_NeverTouchesTheNumbers()
    {
        // A 13D/G cover names the lead reporting person, not the 13F filer's registration.
        var context = Context(
            Filing(
                "0000102909-25-000004",
                "02-SEP-2025",
                "29-AUG-2025",
                crd: "000999999",
                formType: "SCHEDULE 13G"
            )
        );
        var holder = new InstitutionalHolder { Cik = Cik, CrdNumber = "000105958" };

        HoldingsImportService.RefreshHolderIdentityNumbers(context, [holder], NoStoredReports);

        holder.CrdNumber.Should().Be("000105958");
        holder.IdentityAccession.Should().BeNull();
    }

    private static readonly Dictionary<Guid, DateOnly> NoStoredReports = [];

    private static (SubmissionRow Submission, CoverPageRow CoverPage) Filing(
        string accession,
        string filingDate,
        string period,
        string crd,
        string amendmentType = null,
        string formType = null
    ) =>
        (
            new SubmissionRow
            {
                AccessionNumber = accession,
                FilingDate = filingDate,
                PeriodOfReport = period,
                FormType = formType ?? (amendmentType == null ? "13F-HR" : "13F-HR/A"),
                Cik = Cik,
            },
            new CoverPageRow
            {
                AccessionNumber = accession,
                IsAmendment = amendmentType == null ? "N" : "Y",
                AmendmentType = amendmentType,
                Form13FFileNumber = "028-01234",
                CrdNumber = crd,
                SecFileNumber = "801-11953",
            }
        );

    private static ImportContext Context(
        params (SubmissionRow Submission, CoverPageRow CoverPage)[] filings
    ) =>
        new()
        {
            Submissions = filings.ToDictionary(
                f => f.Submission.AccessionNumber,
                f => f.Submission,
                StringComparer.OrdinalIgnoreCase
            ),
            CoverPages = filings.ToDictionary(
                f => f.CoverPage.AccessionNumber,
                f => f.CoverPage,
                StringComparer.OrdinalIgnoreCase
            ),
        };
}
