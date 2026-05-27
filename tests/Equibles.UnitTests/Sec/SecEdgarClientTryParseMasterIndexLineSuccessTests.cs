using System.Reflection;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientTryParseMasterIndexLineSuccessTests
{
    // Contract (TryParseMasterIndexLine XML-doc on ParseMasterIndex,
    // SecEdgarClient.cs:472-475): parses pipe-delimited master.idx rows
    // "CIK|Company Name|Form Type|Date Filed|File Name", keeping only
    // 13F-HR / 13F-HR/A rows with an all-digit CIK. The field layout is:
    //   fields[0] = CIK              (all-digit string)
    //   fields[1] = Company Name     (free text)
    //   fields[2] = Form Type        (must start with "13F-HR")
    //   fields[3] = Date Filed       (yyyy-MM-dd; fallback to caller date
    //                                  on parse failure)
    //   fields[4] = File Name        ("edgar/data/{cik}/{accession}.txt")
    //
    // Existing siblings cover the THREE reject arms — short row (<5
    // fields), non-13F form type, non-numeric CIK. All three exit
    // EARLY before the field-mapping block at lines 527-534 runs.
    // No existing pin exercises the SUCCESS path where every guard
    // passes and TryParseMasterIndexLine populates the
    // EdgarDailyIndexEntry from its specific column.
    //
    // The most damaging refactor risks here are asymmetric and
    // INVISIBLE to every existing sibling:
    //
    //   • Field-index swap — `var company = fields[2]; var formType
    //     = fields[1];` from a copy-paste edit that touched the wrong
    //     line, OR `fields[0]` (CIK) reassigned to CompanyName. The
    //     reject pins all trip earlier guards (length < 5, formType
    //     not "13F-HR", non-numeric CIK) so the field-MAPPING block
    //     is unreached. A swap would compile, pass every reject pin,
    //     and silently corrupt every successfully-parsed 13F-HR row
    //     — the 13F-HR ingest in production goes through this path
    //     for every filing on every quarterly index. Downstream
    //     consequences:
    //       - Form 13F-HR records with CIK in the CompanyName field
    //         would join to no CommonStock (the holdings dashboard's
    //         filer list shows blank entries).
    //       - The accession-number extraction via Path.GetFileName
    //         WithoutExtension(fileName) depends on fields[4]; a
    //         swap would treat e.g. the CIK string as a path and
    //         GetFileNameWithoutExtension would return "1067983"
    //         instead of the real accession.
    //
    //   • Accession-extraction regression — switch to fields[4]
    //     verbatim (skipping Path.GetFileNameWithoutExtension) would
    //     leave "edgar/data/.../0000950123-24-009876.txt" as the
    //     stored AccessionNumber — breaking every dedup key in the
    //     downstream HoldingsImportService (which keys on accession).
    //
    //   • DateFiled fallback misuse — if the regression swapped
    //     `DateOnly.TryParse(dateFiled, ...)` to e.g. always use
    //     fallbackDate, every entry's DateFiled would be the caller-
    //     provided fallback (the daily-index date) instead of the
    //     actual filing date column. The dual assertion on a real
    //     parseable yyyy-MM-dd input AND on a fallbackDate distinct
    //     from the parsed value would catch this — but for minimum
    //     surface, this test pins the success path with the parsed
    //     date matching the field input.
    //
    // Pin: feed a real 13F-HR master.idx row for Berkshire Hathaway
    // (CIK 1067983, the canonical 13F-HR filer used in countless
    // examples across SEC's own docs). Assert each field maps to
    // its documented column. The fallbackDate is set to a value
    // DISTINCT from the parsed date column so a "always use fallback"
    // regression surfaces — DateFiled must come from fields[3],
    // not from the fallback. AccessionNumber asserted as the file
    // basename without extension proves Path.GetFileName
    // WithoutExtension fired correctly.
    //
    // Reflection-invoke since TryParseMasterIndexLine is private
    // static. EdgarDailyIndexEntry is public so direct property
    // access works.
    [Fact]
    public void TryParseMasterIndexLine_ValidThirteenFRow_PopulatesEntryFromDocumentedColumns()
    {
        const string line =
            "1067983|BERKSHIRE HATHAWAY INC|13F-HR|2024-11-14|edgar/data/1067983/0000950123-24-009876.txt";
        var fallbackDate = new DateOnly(1999, 12, 31);

        var method = typeof(SecEdgarClient).GetMethod(
            "TryParseMasterIndexLine",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var args = new object[] { line, fallbackDate, null };
        var success = (bool)method!.Invoke(null, args);

        success.Should().BeTrue();
        var entry = (EdgarDailyIndexEntry)args[2];
        entry.Should().NotBeNull();
        entry!.Cik.Should().Be("1067983");
        entry.CompanyName.Should().Be("BERKSHIRE HATHAWAY INC");
        entry.FormType.Should().Be("13F-HR");
        entry.DateFiled.Should().Be(new DateOnly(2024, 11, 14));
        entry.AccessionNumber.Should().Be("0000950123-24-009876");
    }
}
