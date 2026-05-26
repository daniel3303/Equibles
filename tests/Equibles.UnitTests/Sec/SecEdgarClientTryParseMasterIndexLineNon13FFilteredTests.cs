using System.Reflection;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientTryParseMasterIndexLineNon13FFilteredTests
{
    // TryParseMasterIndexLine's middle guard filters every row whose
    // formType doesn't begin with "13F-HR" — sibling to the existing
    // lowercase-form-type pin (which proves the StartsWith uses
    // OrdinalIgnoreCase). This pin proves the FILTER is actually
    // narrowing the stream. The master.idx daily file mixes every SEC
    // form (10-K, 10-Q, 8-K, S-1, …) on the same lines as 13F filings;
    // the per-day file averages ~30 000 rows of which only a few hundred
    // are 13F variants. A refactor that widened the gate (e.g.
    // `formType.StartsWith("13", …)` or removed the check entirely)
    // would dump every form into Realtime13FIngestionService.
    // DiscoverEntries and flood the import pipeline with non-13F rows
    // that fail downstream type checks. Pin a canonical 10-K row → false.
    [Fact]
    public void TryParseMasterIndexLine_TenKFormType_ReturnsFalse()
    {
        var method = typeof(SecEdgarClient).GetMethod(
            "TryParseMasterIndexLine",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var line =
            "0000320193|Apple Inc.|10-K|2024-11-01|edgar/data/320193/0000320193-24-000123.txt";
        object[] args = [line, new DateOnly(2024, 11, 1), null];

        var success = (bool)method.Invoke(null, args);

        success.Should().BeFalse();
    }
}
