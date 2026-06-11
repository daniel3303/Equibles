using System.Reflection;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Contract (HouseDisclosureClient.cs:321-328): a continuation line "continues
/// the current row's asset name or amount" — the false-positive guards are
/// empty/whitespace, field labels (a colon), and footer notes (a leading '*').
/// The remaining case — the line is not a fresh transaction anchor — is the
/// true positive. A wrapped-amount-only line is the diagnostic input: it has
/// no anchor (no "S/P + MM/DD/YYYY"), no colon, no asterisk, and is the
/// shape a PDF parser produces when an asset description overflows onto the
/// next line and only the dollar range survives.
/// </summary>
public class HouseDisclosureClientIsContinuationLineWrappedAmountTests
{
    [Fact]
    public void IsContinuationLine_WrappedAmountOnly_ClassifiesAsContinuation()
    {
        var method = typeof(HouseDisclosureClient).GetMethod(
            "IsContinuationLine",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, ["$5,000,000"])!;

        result.Should().BeTrue();
    }
}
