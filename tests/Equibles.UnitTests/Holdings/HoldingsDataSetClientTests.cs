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
    public void GetDataSetFileNames_StartIn2024_EmitsLowercaseMonthInNewFormatFilename() {
        // SEC switched the 13F structured data set naming in 2024 from `{year}q{quarter}_form13f.zip`
        // to a date-range form `{ddMMMyyyy}-{ddMMMyyyy}_form13f.zip` where the month abbreviation
        // MUST be lowercase (the SEC's CDN matches the URL case-sensitively — `01Jan2024-...`
        // returns 404 where `01jan2024-...` is the published artifact). Production explicitly
        // calls `.ToLower()` on the `MMM` format result because culture-dependent
        // DateOnly.ToString("MMM") emits a leading-capital `Jan`/`Feb`/etc on every culture
        // that ships, including invariant. Drop the `.ToLower()` (or refactor to a custom
        // formatter that forgets case) and every 2024+ download 404s, silently halting the
        // 13F ingest for the entire post-2023 dataset. The Jan-Feb 2024 transition period
        // is the cleanest pin because it's the ONLY period in the table that uses both the
        // shortest-month "Jan" and the leap-day "29feb" — a regression in either dimension
        // (uppercase month OR wrong leap-day choice) fails the assertion.
        var result = HoldingsDataSetClient.GetDataSetFileNames(new DateTime(2024, 1, 1));

        result.Should().Contain("01jan2024-29feb2024_form13f.zip");
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
