using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Filing13FXmlParserParseFirstIntNullInputTests
{
    // Sibling to Filing13FXmlParserParseFirstIntCommaListTests, which pins the
    // comma-list semantic (split-and-take-first). This pin covers the
    // structurally distinct GUARD arm:
    //   if (string.IsNullOrWhiteSpace(raw)) return null;
    //
    // ParseFirstInt is the bridge from a 13F InfoTable's <otherManager>
    // element to the resolved sequence number. The 13F-HR schema allows the
    // element to be absent on filings that report no other-manager
    // attribution, so `raw == null` is a regular production input — the
    // upstream `Value(Child(info, "otherManager"))` returns null when the
    // element is missing.
    //
    // The risk this pin uniquely catches:
    //   • Drop the null/whitespace guard — `null.Split(',', ...)` throws
    //     NullReferenceException, which propagates up through
    //     ParseInformationTable and aborts the entire batch of holdings
    //     from a single filing missing the otherManager element. Real
    //     SEC 13F-HR filings frequently omit <otherManager> entirely
    //     (the column became fully optional in the 2018-era schema
    //     revision, and even when present, single-manager filings leave
    //     the element empty). Without the guard, every such filing
    //     crashes the import.
    //   • Tighten the guard to `string.IsNullOrEmpty` (drops the
    //     whitespace check) — would compile, pass the comma-list sibling
    //     pin (its input is not whitespace), and crash on filings whose
    //     <otherManager> element is present but contains only whitespace
    //     (a known SEC-publisher quirk on certain redacted submissions).
    //   • Inversion regression — `return raw;` (without null) — would
    //     skip the guard entirely.
    //
    // The complementary risk: a refactor that swapped the early-return
    // null for `return 0` — would change the downstream Parsed13FHolding.
    // OtherManagerNumber from a nullable-null to 0, which the holdings-
    // dashboard then displays as "Manager 0" instead of "no co-filer
    // attribution".
    //
    // Pin: invoke with null and assert (a) no exception thrown AND (b)
    // returned int? is null. The dual assertion distinguishes:
    //   • Working guard: returns null.
    //   • Dropped guard: throws NRE (caught by act.Should().NotThrow()).
    //   • Swap-to-zero: returns 0 (caught by .BeNull()).
    //
    // Reflection-invoke since private static.
    [Fact]
    public void ParseFirstInt_NullInput_ReturnsNullWithoutThrowing()
    {
        var method = typeof(Filing13FXmlParser).GetMethod(
            "ParseFirstInt",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var act = () => (int?)method!.Invoke(null, new object[] { null });

        var result = act.Should().NotThrow().Subject;
        result.Should().BeNull();
    }
}
