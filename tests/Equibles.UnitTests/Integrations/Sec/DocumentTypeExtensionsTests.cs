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
