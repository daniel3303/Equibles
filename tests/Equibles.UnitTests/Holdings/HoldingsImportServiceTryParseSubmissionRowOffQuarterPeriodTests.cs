using System.Reflection;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// The bulk path refuses a Form 13F whose PERIODOFREPORT is not a calendar quarter end; a
// Schedule 13D/G event date is not a quarter end and stays accepted.
public class HoldingsImportServiceTryParseSubmissionRowOffQuarterPeriodTests
{
    private static readonly MethodInfo TryParse = typeof(HoldingsImportService).GetMethod(
        "TryParseSubmissionRow",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    private static (bool Accepted, SubmissionRow Submission) Parse(string form, string period)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SUBMISSIONTYPE"] = form,
            ["ACCESSION_NUMBER"] = "0001062993-20-004026",
            ["PERIODOFREPORT"] = period,
            ["FILING_DATE"] = "14-AUG-2020",
            ["CIK"] = "0001632932",
        };
        object[] args = [row, new DateOnly(2020, 1, 1), null];
        var accepted = (bool)TryParse.Invoke(null, args);
        return (accepted, (SubmissionRow)args[2]);
    }

    [Fact]
    public void TryParseSubmissionRow_Form13FWithOffQuarterPeriod_RefusesTheRow()
    {
        var (accepted, submission) = Parse("13F-HR", "14-AUG-2020");

        accepted.Should().BeFalse();
        submission.Should().BeNull();
    }

    [Fact]
    public void TryParseSubmissionRow_Form13FAmendmentWithOffQuarterPeriod_RefusesTheRow()
    {
        var (accepted, _) = Parse("13F-HR/A", "14-AUG-2020");

        accepted.Should().BeFalse();
    }

    [Fact]
    public void TryParseSubmissionRow_Form13FAtQuarterEnd_KeepsTheRow()
    {
        var (accepted, submission) = Parse("13F-HR", "30-JUN-2020");

        accepted.Should().BeTrue();
        submission.PeriodOfReport.Should().Be("30-JUN-2020");
    }

    [Fact]
    public void TryParseSubmissionRow_Schedule13GWithEventDate_KeepsTheRow()
    {
        var (accepted, submission) = Parse("SCHEDULE 13G", "14-AUG-2020");

        accepted.Should().BeTrue();
        submission.PeriodOfReport.Should().Be("14-AUG-2020");
    }

    [Fact]
    public void IsForm13FOutsideQuarterEnd_UnparseablePeriod_IsFalse()
    {
        // An unparseable period is the existing MinValue skip's job, not this rule's.
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SUBMISSIONTYPE"] = "13F-HR",
            ["PERIODOFREPORT"] = "Q2 2020",
        };

        HoldingsImportService.IsForm13FOutsideQuarterEnd(row).Should().BeFalse();
    }
}
