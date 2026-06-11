using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Contract (XbrlBackfillService.cs:165-179 + 101-115): the SQL-side filter
/// is a broad `Contains("/Archives/edgar/data/")` check, but the helper is
/// the strict gate — it must reject URLs that pass the broad filter but do
/// not end in the `.txt` full-submission form (e.g. an `…-index.htm` page).
/// A non-`.txt` EDGAR URL must derive null, so the backfill's "record the
/// failure and walk it out via the attempt ceiling" path fires on the right
/// rows and not on a phantom accession.
/// </summary>
public class XbrlBackfillServiceDeriveAccessionNumberNonTxtSuffixTests
{
    [Fact]
    public void DeriveAccessionNumber_EdgarPathButHtmlIndexSuffix_ReturnsNull()
    {
        var method = typeof(XbrlBackfillService).GetMethod(
            "DeriveAccessionNumber",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        method.Should().NotBeNull();

        // A standard EDGAR index page for a filing — passes the broad
        // `/Archives/edgar/data/` Contains filter but is not the .txt
        // full-submission URL the helper is wired to recover.
        var url =
            "https://www.sec.gov/Archives/edgar/data/0000320193/0000320193-24-000123-index.htm";

        var result = (string)method!.Invoke(null, [url]);

        result.Should().BeNull();
    }
}
