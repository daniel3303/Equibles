using Equibles.Sec.HostedService.Services;

namespace Equibles.IntegrationTests.Sec;

public class FtdImportServiceTests {
    [Fact]
    public void GetFileNames_StartDateBeforeOldestAvailable_ClampsToJune2017AndSkipsAFileForThatMonth() {
        // FtdImportService.GetFileNames feeds every URL the FTD scraper hits — a regression
        // here means either we miss real data (start too late, skip a month) or we burn 404s
        // hammering the wrong URL (start too early, generate 'a' for June 2017 when only 'b'
        // exists on EDGAR). Two boundary conditions in one method:
        //   (1) startDate is clamped to OldestAvailableDate (2017-06-01) — SEC publishes no
        //       FTD data before that.
        //   (2) For June 2017 specifically, only `cnsfailsYYYYMMb.zip` exists ('a' was never
        //       published); for every other month, both `a` and `b` are generated.
        // This fact passes a deliberately-too-old start date (Jan 2017) to exercise both
        // legs together: the clamp lands on June 2017, and the result for that month must be
        // only the 'b' file. July 2017 must have both — proves the special-case is scoped to
        // June only. The test stays robust against the clock by asserting on the *prefix* of
        // the returned list rather than counts that change as time advances.

        var result = FtdImportService.GetFileNames(new DateOnly(2017, 1, 1));

        // Clamped to June 2017: the first file produced must be the 'b' file for that month,
        // because the 'a' file is skipped. There is NO 'cnsfails201706a.zip' anywhere in the
        // output for this start date.
        result.Should().NotBeEmpty();
        result[0].Should().Be("cnsfails201706b.zip");
        result.Should().NotContain("cnsfails201706a.zip");

        // July 2017 — first month past the special-case — must have BOTH 'a' and 'b' in
        // that exact order, immediately after June's 'b'.
        result.Should().HaveElementAt(1, "cnsfails201707a.zip");
        result.Should().HaveElementAt(2, "cnsfails201707b.zip");
    }
}
