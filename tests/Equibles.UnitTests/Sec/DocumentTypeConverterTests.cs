using System.Globalization;
using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Sec;

public class DocumentTypeConverterTests {
    private readonly DocumentTypeConverter _sut = new();

    [Fact]
    public void ConvertTo_DocumentTypeToString_ReturnsValueNotDisplayName() {
        // MVC tag helpers and Url.Action pass a `DocumentType` as a route value through
        // this converter to produce URL segments like `/Sec/Filings/TenK`. The wire form
        // is the stable internal `Value` (e.g. "TenK"), NOT the human-friendly DisplayName
        // ("10-K") — the slash in "10-K/A" would otherwise break route parsing entirely.
        // A regression that returns DisplayName instead of Value would generate
        // user-visible URLs that 404 on every typed-route action AND break round-tripping
        // through `ConvertFrom` (which looks up by Value via DocumentType.FromValue). Pin
        // ConvertTo on a name with a slash in the DisplayName so the distinction is loud
        // — if a refactor swapped the two properties, the test would fail with a clearly
        // wrong "10-K/A" output instead of the expected "TenKa".
        var result = _sut.ConvertTo(null, CultureInfo.InvariantCulture, DocumentType.TenKa, typeof(string));

        result.Should().Be("TenKa");
    }

    [Fact]
    public void ConvertFrom_KnownValueString_ReturnsMatchingDocumentType() {
        // ConvertTo emits the stable Value ("TenKa"). ConvertFrom is the inverse:
        // MVC route/query binding hands a string like "TenKa" back to this
        // converter, which must round-trip it to the matching DocumentType.
        // The companion ConvertTo test pins emit; this one pins parse. Without
        // it a refactor that switched FromValue to a DisplayName lookup would
        // silently break every route that takes a DocumentType (no exception —
        // null would surface only at use time, far from the binding code).
        var result = _sut.ConvertFrom(null, CultureInfo.InvariantCulture, "TenKa");

        result.Should().Be(DocumentType.TenKa);
    }

    [Fact]
    public void ConvertFrom_UnknownStringValue_ThrowsFormatException() {
        var act = () => _sut.ConvertFrom(null, CultureInfo.InvariantCulture, "NotARealDocumentType");

        act.Should().Throw<FormatException>()
            .WithMessage("*NotARealDocumentType*");
    }
}
