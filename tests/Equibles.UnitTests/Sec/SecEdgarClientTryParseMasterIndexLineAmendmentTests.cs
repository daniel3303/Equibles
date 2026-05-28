using System.Reflection;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientTryParseMasterIndexLineAmendmentTests
{
    // Adversarial Lane A. The ParseMasterIndex XML doc (SecEdgarClient.cs)
    // explicitly says: "keeping only 13F-HR / 13F-HR/A rows". The existing
    // Success test pins `13F-HR` only — a refactor that tightened
    // `StartsWith("13F-HR", IgnoreCase)` to `Equals("13F-HR", IgnoreCase)`
    // would still pass every existing pin (Success, NonNumericCik,
    // ShortRow, Non13FFiltered, LowercaseFormType, MalformedDate) but
    // would silently drop EVERY 13F-HR/A amendment row from the daily
    // index — amendments correct misfiled positions and dropping them
    // leaves the holdings dashboard reporting the original (incorrect)
    // numbers. The amendment arm must remain accepted; FormType on the
    // entry must round-trip exactly as "13F-HR/A".
    [Fact]
    public void TryParseMasterIndexLine_AmendmentFormType_PopulatesEntryWithSlashAFormType()
    {
        const string line =
            "1067983|BERKSHIRE HATHAWAY INC|13F-HR/A|2024-12-02|edgar/data/1067983/0000950123-24-010001.txt";
        var fallbackDate = new DateOnly(1999, 12, 31);

        var method = typeof(SecEdgarClient).GetMethod(
            "TryParseMasterIndexLine",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var args = new object[] { line, fallbackDate, null };
        var success = (bool)method!.Invoke(null, args);

        success.Should().BeTrue("amendments are explicitly listed in the doc-comment");
        var entry = (EdgarDailyIndexEntry)args[2];
        entry.Should().NotBeNull();
        entry!.FormType.Should().Be("13F-HR/A", "amendment suffix must survive intact");
        entry.Cik.Should().Be("1067983");
        entry.AccessionNumber.Should().Be("0000950123-24-010001");
    }
}
