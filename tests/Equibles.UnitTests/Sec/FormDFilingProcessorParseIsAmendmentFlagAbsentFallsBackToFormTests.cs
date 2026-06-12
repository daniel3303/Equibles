using System.Reflection;
using System.Xml.Linq;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Contract (FormDFilingProcessor.cs:154-162): the XML flag under
/// <c>typeOfFiling/newOrAmendment/isAmendment</c> is authoritative when
/// present. When the flag is absent (no <c>newOrAmendment</c> child), the
/// submission's <c>Form</c> string decides — "<c>D/A</c>" is an amendment,
/// "<c>D</c>" is not. The fallback path is what the SEC's pre-XML
/// <c>edgarSubmission</c> submissions rely on, and the only path that has
/// never been pinned: every existing Form D test goes through the flag.
/// A regression that returned false on the missing-flag branch (e.g. a
/// refactor that early-returned instead of falling through) would silently
/// re-classify every legacy amendment as an original and break the
/// upsert-instead-of-insert invariant downstream.
/// </summary>
public class FormDFilingProcessorParseIsAmendmentFlagAbsentFallsBackToFormTests
{
    [Fact]
    public void ParseIsAmendment_NoNewOrAmendmentBlock_FormSlashAReturnsTrue()
    {
        // typeOfFiling has no <newOrAmendment> child — the fallback must fire.
        var typeOfFiling = new XElement("typeOfFiling");
        var filing = new FilingData { Form = "D/A" };

        var result = InvokeParseIsAmendment(typeOfFiling, filing);

        result.Should().BeTrue();
    }

    [Fact]
    public void ParseIsAmendment_NoNewOrAmendmentBlock_FormWithoutSlashAReturnsFalse()
    {
        // Mirror of the above for the non-amendment arm: no flag, no "/A" in
        // the form, must be false — not true by default. Catches a regression
        // that returned the fallback's negation, or hard-coded true.
        var typeOfFiling = new XElement("typeOfFiling");
        var filing = new FilingData { Form = "D" };

        var result = InvokeParseIsAmendment(typeOfFiling, filing);

        result.Should().BeFalse();
    }

    private static bool InvokeParseIsAmendment(XElement typeOfFiling, FilingData filing)
    {
        var method = typeof(FormDFilingProcessor).GetMethod(
            "ParseIsAmendment",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        method.Should().NotBeNull();

        return (bool)method!.Invoke(null, [typeOfFiling, filing])!;
    }
}
