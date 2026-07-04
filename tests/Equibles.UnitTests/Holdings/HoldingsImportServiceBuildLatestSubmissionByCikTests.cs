using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceBuildLatestSubmissionByCikTests
{
    // A bulk data set carries several submissions per filer (late filings for older
    // quarters land in the same archive). The confidential-treatment refresh must
    // read the LATEST-filed cover page per CIK — parsed FilingDate first (the raw
    // strings are SEC dd-MMM-yyyy, where an ordinal sort misorders month spans),
    // accession breaking same-day ties — never an arbitrary first match.
    [Fact]
    public void BuildLatestSubmissionByCik_MultipleSubmissionsPerCik_PicksLatestFiled()
    {
        var older = Submission("0000000000-25-000001", "29-JAN-2025", "1000");
        var latest = Submission("0000000000-25-000002", "14-FEB-2025", "1000");
        var otherCik = Submission("0000000000-25-000003", "01-JAN-2025", "2000");

        var result = HoldingsImportService.BuildLatestSubmissionByCik([latest, older, otherCik]);

        result.Should().HaveCount(2);
        result["1000"].Should().BeSameAs(latest);
        result["2000"].Should().BeSameAs(otherCik);
    }

    [Fact]
    public void BuildLatestSubmissionByCik_SameDayAmendment_HigherAccessionWins()
    {
        var original = Submission("0000000000-25-000010", "14-FEB-2025", "1000");
        var amendment = Submission("0000000000-25-000011", "14-FEB-2025", "1000");

        var result = HoldingsImportService.BuildLatestSubmissionByCik([amendment, original]);

        result["1000"].Should().BeSameAs(amendment);
    }

    [Fact]
    public void BuildLatestSubmissionByCik_MissingCik_IsIgnored()
    {
        var noCik = Submission("0000000000-25-000020", "14-FEB-2025", null);

        var result = HoldingsImportService.BuildLatestSubmissionByCik([noCik]);

        result.Should().BeEmpty();
    }

    private static SubmissionRow Submission(string accession, string filingDate, string cik) =>
        new()
        {
            AccessionNumber = accession,
            FilingDate = filingDate,
            Cik = cik,
        };
}
