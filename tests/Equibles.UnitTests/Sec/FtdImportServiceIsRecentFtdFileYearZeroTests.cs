using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FtdImportServiceIsRecentFtdFileYearZeroTests
{
    // Contract (doc-comment + explicit validation gates): IsRecentFtdFile returns
    // true iff the embedded YYYYMM is within the last 2 months, returns false for
    // any malformed input, and never throws. The body validates length and gates
    // `month is >= 1 and <= 12`, but does not gate year >= 1. `int.TryParse("0000",
    // out year)` succeeds with year=0, both span parses pass, and execution falls
    // through to `new DateOnly(0, 1, 1)` — whose constructor requires year >= 1
    // and throws ArgumentOutOfRangeException. A filename whose YYYY slice is all
    // zeros (a corrupted/zero-prefixed filename surfacing from SEC, a mirror, or
    // any caller that hands the helper an attacker-shaped or accidentally-empty
    // string) crashes the scrape cycle instead of being silently rejected.
    [Fact(Skip = "GH-1350 — IsRecentFtdFile throws on year=0 instead of returning false")]
    public void IsRecentFtdFile_YearZero_ReturnsFalse()
    {
        var result = FtdImportService.IsRecentFtdFile("cnsfails000001a.zip");

        result.Should().BeFalse();
    }
}
