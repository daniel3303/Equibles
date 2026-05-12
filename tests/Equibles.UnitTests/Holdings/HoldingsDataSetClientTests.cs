using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsDataSetClientTests {
    [Fact]
    public void GetDataSetFileNames_StartDateBeforeEarliestAvailable_ClampsToQ2_2013() {
        var result = HoldingsDataSetClient.GetDataSetFileNames(new DateTime(2010, 1, 1));

        result.Should().Contain("2013q2_form13f.zip");
        result.Should().NotContain("2013q1_form13f.zip");
    }

    [Fact]
    public void GetDataSetFileNames_StartIn2014_ProducesOldQuarterlyFormatFilename() {
        // The SEC publishes Form 13F data sets under exact-cased filenames that the
        // downloader concatenates onto the BaseUrl verbatim. For 2013-2023 the format
        // is `{year}q{quarter}_form13f.zip` (lowercase `q`). A refactor that changed
        // the format string — uppercase Q, removing the underscore, dropping the
        // four-digit year — would 404 every old-period download and silently halt
        // the entire 13F ingest for the decade of historical data. The clamping
        // test above only proves boundary behaviour; this one pins the literal
        // filename SEC actually serves.
        var result = HoldingsDataSetClient.GetDataSetFileNames(new DateTime(2014, 1, 15));

        result.Should().Contain("2014q1_form13f.zip");
    }
}
