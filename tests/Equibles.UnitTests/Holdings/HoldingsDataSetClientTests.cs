using System.Reflection;
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
    public void GetDataSetFileNames_DecToFebPeriodSpanningNonLeapYear_UsesFeb28() {
        // The regular post-2024 cycle ends each year with a Dec→Feb period whose
        // last day depends on whether the FOLLOWING calendar year is a leap year:
        //   periods.Add((new DateOnly(year, 12, 1),
        //       new DateOnly(year + 1, 2, DateTime.IsLeapYear(year + 1) ? 29 : 28)));
        // The existing `01jan2024-29feb2024` pin doesn't exercise this branch — that
        // period is a one-time 2024-transition entry with the `29` hardcoded
        // literally, NOT computed via `DateTime.IsLeapYear`. The regular cycle's
        // leap/non-leap toggle is otherwise unpinned.
        //
        // The risk this catches is asymmetric and reaches every other year: a
        // regression that hard-coded `28` everywhere would silently mis-name the
        // Dec→Feb URL for periods where year+1 IS leap (Dec 2027 → Feb 2028,
        // Dec 2031 → Feb 2032, etc.). A regression that hard-coded `29` would
        // mis-name every NON-leap-ending period (Dec 2024 → Feb 2025, Dec 2025 →
        // Feb 2026, Dec 2026 → Feb 2027, etc. — 3 of every 4 cycles).
        //
        // SEC's CDN matches the URL case- AND day-number-sensitively: `01dec2024-29feb2025`
        // returns 404 where `01dec2024-28feb2025` is the published artifact. A
        // single off-by-one in the day-number silently halts ingest of one entire
        // 3-month 13F-HR period — the most recent quarterly disclosures, which
        // are what dashboards and analyst tools refresh against first. The
        // failure mode is invisible past the 404 (HoldingsScraperWorker logs the
        // download error per file and continues; ProcessedDataSet never marks
        // the missing period; the gap silently persists across cycles).
        //
        // Construction: start from 2025-01-01 (after the 2024 transition period's
        // window so no leap-year hardcoded entry confuses the assertion) and
        // assert the Dec 2025 → Feb 2026 period's filename. 2026 is non-leap
        // (Feb 28); a regression to hardcoded `29` would yield `29feb2026` and
        // fail this assertion. The pair (existing `01jan2024-29feb2024` for the
        // 2024-transition hardcoded literal + this one for the IsLeapYear=false
        // branch of the regular cycle) covers both day-number sources.
        var result = HoldingsDataSetClient.GetDataSetFileNames(new DateTime(2025, 1, 1));

        result.Should().Contain("01dec2025-28feb2026_form13f.zip");
    }

    [Fact]
    public void GetNewFormatPeriods_DecToFebPeriodSpanningLeapYear_UsesFeb29() {
        // Sibling to `GetDataSetFileNames_DecToFebPeriodSpanningNonLeapYear_UsesFeb28`.
        // That pin covers the IsLeapYear=false arm of the Dec→Feb ternary:
        //   new DateOnly(year + 1, 2, DateTime.IsLeapYear(year + 1) ? 29 : 28))
        // This pin covers the IsLeapYear=true arm — the OTHER side of the same
        // ternary that's currently UNREACHABLE through the public
        // `GetDataSetFileNames` entry point given today's date.
        //
        // Why unreachable from the public API: the IsLeapYear=true arm fires
        // for periods like Dec 2027 → Feb 29 2028 (year+1=2028, leap). The
        // public method gates emissions on `end >= nowDate`, so any future
        // leap-spanning period is skipped until that February has actually
        // happened. Past leap years either fall before 2024 (the regular
        // cycle's startYear floor) or are masked by the hard-coded 2024
        // transition period — that one explicitly writes `Feb 29 2024` as a
        // literal, NOT via DateTime.IsLeapYear, so it doesn't exercise the
        // ternary either.
        //
        // The risk this catches: a refactor that hard-codes `28` (the
        // non-leap day) in the ternary — easy to do during a "tidy up the
        // leap-year complication" pass since the only leap-year period in
        // the test corpus is the hardcoded 2024 transition — would compile,
        // pass the existing pins (the non-leap arm assertion + the hardcoded
        // 2024 transition assertion), and silently mis-name the Dec 2027 →
        // Feb 2028 URL once that period publishes in early 2028. SEC's CDN
        // returns 404 on the wrong day-number, HoldingsScraperWorker logs
        // and continues, and the Q1 2028 13F-HR window silently fails to
        // ingest — an invisible gap that persists until manual
        // intervention.
        //
        // Use reflection on the private static GetNewFormatPeriods so we can
        // inject a "now" in the future (2029-01-01) and force the Dec 2027 →
        // Feb 29 2028 period into the emitted list. The non-leap sibling
        // tests the public API path; this one targets the ternary directly.
        // The pair (non-leap from 2025-01-01 + leap from injected 2029-01-01)
        // covers both arms.
        var method = typeof(HoldingsDataSetClient).GetMethod(
            "GetNewFormatPeriods", BindingFlags.NonPublic | BindingFlags.Static);
        var fakeNow = new DateTime(2029, 1, 1);

        var result = (List<string>)method!.Invoke(null, [2024, fakeNow]);

        result.Should().Contain("01dec2027-29feb2028_form13f.zip");
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
