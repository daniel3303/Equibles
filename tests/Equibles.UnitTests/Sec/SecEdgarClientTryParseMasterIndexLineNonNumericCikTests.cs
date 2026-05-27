using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientTryParseMasterIndexLineNonNumericCikTests
{
    // The inline comment on the CIK guard names its purpose verbatim:
    // "Header/preamble rows ('CIK', 'Company Name', dashes) fail this."
    // The defensive `cik.All(char.IsDigit)` rule defends against corrupt or
    // mis-aligned master.idx payloads (a column drift that pushes a text
    // header into the CIK slot, or a partially-truncated download where
    // a continuation line lands on the wrong field). A refactor that
    // weakened this to a length check or removed it would let an entry
    // with `Cik = "CIK"` (or any non-numeric token) reach the downstream
    // EdgarDailyIndexEntry — and every fetch using that CIK would 404 from
    // SEC, polluting the daily-index sweep with garbage rows. Pin: a
    // 13F-HR-prefixed line with non-digit CIK returns false.
    [Fact]
    public void TryParseMasterIndexLine_NonNumericCikInOtherwiseValid13FRow_ReturnsFalse()
    {
        var method = typeof(SecEdgarClient).GetMethod(
            "TryParseMasterIndexLine",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var line = "abc123|Example Fund|13F-HR|2024-11-01|edgar/data/0/0000000000-24-000123.txt";
        object[] args = [line, new DateOnly(2024, 11, 1), null];

        var success = (bool)method.Invoke(null, args);

        success.Should().BeFalse();
    }
}
