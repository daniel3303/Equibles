using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class HoldingsExportControllerFormatNullablePercentNullEmptyTests
{
    // FormatNullablePercent is unpinned despite being on the export hot
    // path (called twice per row in HoldingsExportController.Holders for
    // ChangePercent and OwnershipPercent — both `double?` because a
    // change/ownership ratio is undefined when the denominator is zero
    // or unknown).
    //
    // The contract is the ternary on `value.HasValue`:
    //     value.HasValue
    //         ? value.Value.ToString("F2", InvariantCulture)
    //         : string.Empty
    //
    // The MISSING-VALUE arm (null → empty cell) is what this pin defends.
    // The CSV consumer (analyst spreadsheets, downstream BI tooling)
    // distinguishes "no value reported" from "value is zero" via the
    // EMPTY-CELL convention — "0.00" means "the filer holds exactly 0%
    // of float", while "" means "we don't know the share count to
    // compute the ratio". Conflating them silently misreports issuers
    // with unknown share counts as "no filer holds any of this stock"
    // — a real signal that should drive different operator action
    // (data-quality investigation vs. trade analysis).
    //
    // The risks this pin uniquely catches:
    //
    //   • Wrong-default regression — `value?.ToString("F2", Invariant)
    //     ?? "0.00"` (under "consistency — every cell should show a
    //     number") would compile, work for every non-null caller, and
    //     silently fabricate a "zero percent" signal for every
    //     unknown-shares row.
    //
    //   • HasValue drop — `value.Value.ToString(...)` (under "the
    //     caller always passes a value") would throw
    //     InvalidOperationException on every null input — crashing
    //     the Holders CSV export for any stock whose
    //     SharesOutstanding == 0 (the trigger for null
    //     OwnershipPercent per its calculation contract).
    //
    //   • Wrong literal for missing — `?? "—"` or `?? "N/A"` would
    //     also fail the BeEmpty assertion. The CSV column header
    //     promises a numeric field; a non-empty non-numeric value
    //     breaks downstream Excel/Pandas type inference for the
    //     whole column.
    //
    // Pin: invoke FormatNullablePercent(null) via reflection (private
    // static); assert result is the empty string. Distinguishes the
    // three regressions:
    //   • Working: returns "".
    //   • Wrong-default ("0.00"): fails BeEmpty.
    //   • HasValue drop: throws on Invoke (reflection bubbles
    //     TargetInvocationException).
    //   • Wrong literal ("—", "N/A"): fails BeEmpty.
    [Fact]
    public void FormatNullablePercent_NullInput_ReturnsEmptyStringNotZeroOrPlaceholder()
    {
        var method = typeof(HoldingsExportController).GetMethod(
            "FormatNullablePercent",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, [(double?)null]);

        result
            .Should()
            .BeEmpty(
                "empty cell signals 'no value reported'; '0.00' would conflate with an actual reported-zero ratio"
            );
    }
}
