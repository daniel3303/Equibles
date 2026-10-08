using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// The holder's confidential-treatment flag comes from SUMMARYPAGE.ISCONFIDENTIALOMITTED of its
/// newest report: the latest original or RESTATEMENT for the newest period. Shapes mirror
/// Berkshire Hathaway's EDGAR history, where one day carried a new quarter's original and two
/// NEW HOLDINGS amendments disclosing older confidential positions.
/// </summary>
public class HoldingsImportServiceLatestReportConfidentialOmissionTests
{
    private const string Cik = "1067983";

    [Fact]
    public void SameDayNewHoldingsAmendmentForAnOlderQuarter_DoesNotSpeakForTheNewestReport()
    {
        var context = Context(
            Filing("0000950123-24-005622", "15-MAY-2024", "31-MAR-2024", omitted: true),
            Filing(
                "0000950123-24-005664",
                "15-MAY-2024",
                "31-DEC-2023",
                omitted: false,
                amendmentType: "NEW HOLDINGS"
            )
        );

        HoldingsImportService.BuildLatestReportConfidentialOmission(context)[Cik].Should().BeTrue();
    }

    [Fact]
    public void LateRestatementOfAnOlderQuarter_DoesNotReplaceTheNewestPeriod()
    {
        var context = Context(
            Filing("0000950123-26-000001", "14-AUG-2026", "30-JUN-2026", omitted: true),
            Filing(
                "0000950123-26-000002",
                "20-AUG-2026",
                "31-MAR-2026",
                omitted: false,
                amendmentType: "RESTATEMENT"
            )
        );

        HoldingsImportService.BuildLatestReportConfidentialOmission(context)[Cik].Should().BeTrue();
    }

    [Fact]
    public void RestatementOfTheNewestPeriod_ReplacesItsOriginal()
    {
        var context = Context(
            Filing("0000950123-23-010898", "14-NOV-2023", "30-SEP-2023", omitted: true),
            Filing(
                "0000950123-23-011029",
                "16-NOV-2023",
                "30-SEP-2023",
                omitted: false,
                amendmentType: "RESTATEMENT"
            )
        );

        HoldingsImportService
            .BuildLatestReportConfidentialOmission(context)[Cik]
            .Should()
            .BeFalse();
    }

    [Fact]
    public void ReportWithoutAStatedFlag_LeavesTheStoredValue()
    {
        var context = Context(
            Filing("0000950123-26-000003", "14-AUG-2026", "30-JUN-2026", omitted: null)
        );
        var holder = new InstitutionalHolder { Cik = Cik, ConfidentialTreatmentRequested = true };

        Service().RefreshExistingHolderConfidentialTreatment(context, [holder]);

        holder.ConfidentialTreatmentRequested.Should().BeTrue();
    }

    [Fact]
    public void MixedCikSpellings_RefreshFromTheNewestReport()
    {
        var context = Context(
            Filing("0000950123-25-000023", "14-NOV-2025", "30-SEP-2025", omitted: false),
            Filing(
                "0000950123-26-000024",
                "14-FEB-2026",
                "31-DEC-2025",
                omitted: true,
                cik: "0001067983"
            )
        );
        var holder = new InstitutionalHolder { Cik = Cik, ConfidentialTreatmentRequested = false };

        Service().RefreshExistingHolderConfidentialTreatment(context, [holder]);

        holder.ConfidentialTreatmentRequested.Should().BeTrue();
    }

    private static (SubmissionRow Submission, CoverPageRow CoverPage, bool? Omitted) Filing(
        string accession,
        string filingDate,
        string period,
        bool? omitted,
        string amendmentType = null,
        string cik = Cik
    ) =>
        (
            new SubmissionRow
            {
                AccessionNumber = accession,
                FilingDate = filingDate,
                PeriodOfReport = period,
                FormType = amendmentType == null ? "13F-HR" : "13F-HR/A",
                Cik = cik,
            },
            new CoverPageRow
            {
                AccessionNumber = accession,
                IsAmendment = amendmentType == null ? "N" : "Y",
                AmendmentType = amendmentType,
            },
            omitted
        );

    private static ImportContext Context(
        params (SubmissionRow Submission, CoverPageRow CoverPage, bool? Omitted)[] filings
    )
    {
        var context = new ImportContext
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
        foreach (var filing in filings.Where(f => f.Omitted.HasValue))
            context.ConfidentialOmittedByAccession[filing.Submission.AccessionNumber] =
                filing.Omitted.Value;
        return context;
    }

    private static HoldingsImportService Service() =>
        new(
            Substitute.For<IServiceScopeFactory>(),
            NullLogger<HoldingsImportService>.Instance,
            Options.Create(new WorkerOptions()),
            Substitute.For<IStockPriceProvider>(),
            Substitute.For<MassTransit.IBus>()
        );
}
