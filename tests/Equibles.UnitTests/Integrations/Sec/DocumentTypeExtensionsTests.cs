using System.Reflection;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Integrations.Sec;

/// <summary>
/// Tests for <c>Equibles.Integrations.Sec.Extensions.DocumentTypeExtensions</c>.
/// The class is <c>internal</c> and the project has no <c>InternalsVisibleTo</c>,
/// so we invoke <c>GetFormName</c> through reflection.
/// </summary>
public class DocumentTypeExtensionsTests {
    private static readonly MethodInfo GetFormNameMethod = typeof(DocumentTypeFilter).Assembly
        .GetType("Equibles.Integrations.Sec.Extensions.DocumentTypeExtensions")
        .GetMethod("GetFormName", BindingFlags.Public | BindingFlags.Static);

    [Fact]
    public void GetFormName_UndefinedEnumValue_FallsBackToToStringNumericRepresentation() {
        // GetFormName chains `?.GetCustomAttribute<DisplayAttribute>()?.Name ??
        // documentType.ToString()`. When the enum value isn't a defined
        // member, GetField returns null, the attribute lookup short-circuits,
        // and the fallback returns ToString (which for an undefined value is
        // its numeric form). Every built-in DocumentTypeFilter member carries
        // [Display], so the fallback only fires when a future enum member is
        // added without the attribute OR — defensively — when EDGAR
        // unexpectedly returns a form code mapped to an out-of-range enum
        // value. Pin the fallback so a refactor that drops the `??` (or
        // narrows the null-conditional chain) surfaces as a clear test
        // failure rather than a runtime NRE in SecEdgarClient's form filter.
        var undefined = (DocumentTypeFilter)999;

        var result = (string)GetFormNameMethod.Invoke(null, [undefined]);

        result.Should().Be("999");
    }

    [Fact]
    public void GetFormName_FormFour_ReturnsSecWireValue() {
        // SecEdgarClient filters EDGAR API responses by `f.Form == documentType.Value.GetFormName()`.
        // The C# enum value `FormFour` carries `[Display(Name = "4")]`, and SEC's wire format for
        // the Form 4 insider-transaction filing is the literal string "4". If GetFormName ever
        // returned the enum's ToString ("FormFour") because the reflection lookup of the
        // [Display] attribute broke, the equality check would never match and the Form 4
        // ingest pipeline would silently stop pulling insider-transaction filings. Pin the
        // wire value so a refactor of the attribute-lookup path can't break the SEC filter.
        var result = (string)GetFormNameMethod.Invoke(null, [DocumentTypeFilter.FormFour]);

        result.Should().Be("4");
    }
}
