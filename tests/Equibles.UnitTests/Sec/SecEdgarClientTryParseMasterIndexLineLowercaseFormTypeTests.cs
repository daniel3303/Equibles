using System.Reflection;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientTryParseMasterIndexLineLowercaseFormTypeTests
{
    private static readonly MethodInfo TryParseMasterIndexLineMethod =
        typeof(SecEdgarClient).GetMethod(
            "TryParseMasterIndexLine",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // The newly-extracted TryParseMasterIndexLine helper (#1459) gates the
    // form-type field with `StartsWith("13F-HR", StringComparison.OrdinalIgnoreCase)`.
    // The existing integration tests (GetDailyIndex_MixedMasterIndex_…) only
    // feed uppercase form types — the documented case-insensitive contract is
    // unexercised. A regression deleting the StringComparison argument would
    // fall back to `StringComparison.CurrentCulture` (whose case sensitivity
    // varies by locale, but is case-sensitive on every culture the CLR ships
    // with) and silently drop any defensively-lower-cased SEC row from every
    // 13F sweep. A regression to explicit `Ordinal` would do the same.
    [Fact]
    public void TryParseMasterIndexLine_LowercaseFormType13fHr_ReturnsTrueWithEntry()
    {
        var line =
            "1067983|BERKSHIRE HATHAWAY INC|13f-hr|2024-11-20|edgar/data/1067983/0000950123-24-006477.txt";
        var args = new object[] { line, new DateOnly(2024, 11, 20), null };

        var parsed = (bool)TryParseMasterIndexLineMethod.Invoke(null, args);

        parsed.Should().BeTrue();
        var entry = (EdgarDailyIndexEntry)args[2];
        entry.Should().NotBeNull();
        entry.AccessionNumber.Should().Be("0000950123-24-006477");
        entry.Cik.Should().Be("1067983");
    }
}
