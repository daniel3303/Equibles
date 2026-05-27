using System.Reflection;
using System.Xml.Linq;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorGetWrappedValueNullPathTests
{
    // GetWrappedValue is the path-walker every Form 4 field traversal flows
    // through — directOrIndirectOwnership, transactionAmounts/transactionShares,
    // transactionCoding/transactionCode, and the conversion/exerciseDate path on
    // derivative transactions. Its contract is "walk the path through `?.` so
    // any missing intermediate element returns null instead of throwing"; the
    // body uses the null-conditional chain `element = element?.Element(name)`
    // exactly for this reason.
    //
    // The risk this pin uniquely catches: GetWrappedValue is private static and
    // currently untested. A "tidy the foreach" refactor that drops the `?.` to
    // plain `.Element(name)` — under the (false) intuition that the loop's
    // initialization with `var element = parent` guarantees non-null — would
    // compile, work for present-path inputs (e.g. a complete ownership XML),
    // and NRE the moment a Form 4 omits an intermediate element. Real SEC
    // ownership-XML variants do this: legacy 3/A and 4/A filings, post-
    // correction restatements with stub elements, partial-data filings from
    // edge-case filers. A single NRE in GetWrappedValue propagates up through
    // ParseTransaction → the insider-trading filing import loop, aborts the
    // batch, and skips every remaining filing in the worker cycle.
    //
    // The complementary risk: a refactor that swapped `?.Element("value")?.Value`
    // for `.Element("value").Value` on the final read — same root cause, same
    // crash mode, this time on filings whose terminal `<value>` element is
    // absent (the path resolves but the wrapper itself is empty).
    //
    // Construction: pass an XElement that has the FIRST path element present
    // but lacks the SECOND. Walking the second step lands on null; the next
    // iteration's `null?.Element(...)` short-circuits to null; the final
    // `null?.Element("value")?.Value` short-circuits again. The contract
    // promises null — without the `?.` chain the call would throw
    // NullReferenceException.
    //
    // The dual assertion (.NotThrow + .BeNull) defends against both regression
    // classes: dropping the `?.` from the foreach (throws), and dropping the
    // `?.` from the final read (also throws — same Subject would surface here
    // because `null?.Element("value")?.Value` is the final null-tolerant
    // segment too).
    //
    // Reflection-invoke since the method is private static.
    [Fact]
    public void GetWrappedValue_PathMissingMidWalk_ReturnsNullWithoutThrowing()
    {
        var method = typeof(InsiderTradingFilingProcessor).GetMethod(
            "GetWrappedValue",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        // Parent contains the first path element but NOT the second — the walk
        // must short-circuit to null without NRE.
        var parent = new XElement(
            "nonDerivativeTransaction",
            new XElement(
                "transactionCoding"
            // intentionally no "transactionCode" child
            )
        );
        var pathArg = new[] { "transactionCoding", "transactionCode" };

        var act = () => (string)method!.Invoke(null, [parent, pathArg]);

        var result = act.Should().NotThrow().Subject;
        result.Should().BeNull();
    }
}
