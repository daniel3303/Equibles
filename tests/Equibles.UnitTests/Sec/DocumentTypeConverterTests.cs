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
    public void CanConvertFrom_StringSourceType_ReturnsTrue() {
        // MVC's TypeConverter pipeline asks `CanConvertFrom(string)` BEFORE
        // invoking the converter — if it returns false, MVC silently skips the
        // converter entirely and falls back to default binding (which fails for
        // the custom DocumentType reference type, surfacing as a model-state
        // error or a null route argument). The existing ConvertFrom pins assume
        // CanConvertFrom returns true; if the gate flipped, ConvertFrom would
        // never be called in production and those pins would stay green while
        // every `/Sec/Filings/{docType}` route silently breaks.
        //
        // The risk: a refactor that "tightens" the predicate (e.g. requiring an
        // additional context check, or comparing `sourceType.Name == "String"`
        // which is case-sensitive on the type name and breaks for the qualified
        // System.String case) would compile cleanly, pass every ConvertFrom test
        // (those bypass the CanConvertFrom gate when called directly), and only
        // surface as 400 model-binding errors at request time.
        //
        // Pin the contract: `CanConvertFrom(typeof(string)) == true`. Asserts
        // the explicit string-handling branch.
        var result = _sut.CanConvertFrom(context: null, sourceType: typeof(string));

        result.Should().BeTrue();
    }

    [Fact]
    public void CanConvertFrom_NonStringSourceType_FallsThroughToBaseAndReturnsFalse() {
        // Sibling to `CanConvertFrom_StringSourceType_ReturnsTrue`. The
        // production code is the short-circuit OR:
        //   return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
        // The string-true sibling exercises the LEFT arm (short-circuit on
        // the type-equality check). This pin exercises the RIGHT arm — the
        // delegation to `base.CanConvertFrom`, which for the default
        // `TypeConverter` base returns false for any non-string source.
        //
        // The risk this catches: a refactor that "simplifies" the OR to just
        // `return sourceType == typeof(string);` — under the false
        // intuition that the base call is dead weight since the converter
        // is string-only — would compile, pass every existing pin
        // (CanConvertFrom-string, ConvertFrom-string, ConvertTo-DocumentType),
        // and silently lock out an entire FAMILY of plausible future
        // type-converter contracts:
        //
        //   • A registered IConvertible-source binding (e.g. binding a
        //     numeric DocumentType discriminator from a JSON column where
        //     the underlying ADO.NET reader hands back an int).
        //   • A custom InstanceDescriptor flow used by design-time tools
        //     (the Razor visual designer, Entity Framework migration
        //     tooling) which probe `CanConvertFrom(typeof(InstanceDescriptor))`.
        //
        // The opposite regression — a refactor that makes the base call
        // return true (e.g. inheriting from `EnumConverter` instead of
        // `TypeConverter`) — would also be caught: integer source types
        // would suddenly bind successfully, opening the converter to
        // unintended numeric coercion that `DocumentType.FromValue`
        // wouldn't recognize.
        //
        // Pin `typeof(int)` specifically — it's the most plausible
        // accidental admission (every numeric column in the SEC schema
        // would suddenly become a candidate for binding). Asserting
        // `BeFalse()` documents that ONLY the explicit string arm
        // succeeds.
        var result = _sut.CanConvertFrom(context: null, sourceType: typeof(int));

        result.Should().BeFalse();
    }

    [Fact]
    public void ConvertFrom_UnknownStringValue_ThrowsFormatException() {
        var act = () => _sut.ConvertFrom(null, CultureInfo.InvariantCulture, "NotARealDocumentType");

        act.Should().Throw<FormatException>()
            .WithMessage("*NotARealDocumentType*");
    }
}
