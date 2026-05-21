using System.Reflection;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Integrations.Sec;

public class SecEdgarClientTryParseMasterIndexLineMalformedDateTests
{
    private static readonly MethodInfo TryParseMethod = typeof(SecEdgarClient).GetMethod(
        "TryParseMasterIndexLine",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    [Fact]
    public void TryParseMasterIndexLine_DateFiledUnparseable_FallsBackToProvidedDate()
    {
        // SecEdgarClient.ParseMasterIndex feeds every line of EDGAR's
        // /Archives/edgar/daily-index/{year}/QTR{q}/master.{yyyyMMdd}.idx
        // through TryParseMasterIndexLine. The file's own URL carries the
        // canonical filing date — passed in as `fallbackDate` — so when a
        // single row's DateFiled field is corrupt (an upstream feed glitch,
        // a transient mid-stream truncation, a culture-mismatched
        // re-render), the helper must STILL emit an entry and fall back to
        // the file's URL date rather than dropping the row or aborting the
        // whole sweep.
        //
        // The risk this catches: a refactor that "tightens" the date parse
        // — say, replacing `DateOnly.TryParse(dateFiled, out var d) ? d :
        // fallbackDate` with the prettier-looking
        // `DateOnly.Parse(dateFiled)` — would compile, pass every existing
        // test (none of which feed a malformed date), and on the first
        // corrupt master.idx row throw `FormatException` out of
        // ParseMasterIndex, aborting the entire daily-index sweep.
        // Real-time 13F ingestion (Realtime13FIngestionService.DiscoverEntries)
        // depends on this loop completing, so a thrown parse failure
        // doesn't just lose ONE row — it loses every row for that day's
        // file and every 13F amendment that hadn't been processed yet.
        //
        // Pin the fallback arm. Build a syntactically valid 13F-HR line
        // (digits-only CIK, accession-shaped artifact path) where the
        // dateFiled column holds a value DateOnly.TryParse rejects.
        // Expected: the helper returns true, the out-entry is populated,
        // and DateFiled equals the supplied fallback.
        var fallback = new DateOnly(2026, 5, 15);
        var line =
            "0001234567|Example Fund LLC|13F-HR|not-a-date|edgar/data/1234567/0001234567-26-000123.txt";

        object[] args = [line, fallback, null];
        var success = (bool)TryParseMethod.Invoke(null, args);
        var entry = (EdgarDailyIndexEntry)args[2];

        success.Should().BeTrue();
        entry.Should().NotBeNull();
        entry.DateFiled.Should().Be(fallback);
        entry.Cik.Should().Be("0001234567");
        entry.FormType.Should().Be("13F-HR");
        entry.AccessionNumber.Should().Be("0001234567-26-000123");
    }
}
