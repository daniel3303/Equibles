using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Sec;

public class DocumentTypeTests {
    [Theory]
    [InlineData("TenK")]
    [InlineData("TenQ")]
    [InlineData("EightK")]
    [InlineData("FormFour")]
    public void FromValue_ReturnsCorrectType(string value) {
        var result = DocumentType.FromValue(value);

        result.Should().NotBeNull();
        result.Value.Should().Be(value);
    }

    [Theory]
    [InlineData("tenk", "TenK")]
    [InlineData("TENK", "TenK")]
    [InlineData("eightk", "EightK")]
    public void FromValue_IsCaseInsensitive(string input, string expectedValue) {
        var result = DocumentType.FromValue(input);

        result.Should().NotBeNull();
        result.Value.Should().Be(expectedValue);
    }

    [Fact]
    public void FromValue_UnknownValue_ReturnsNull() {
        var result = DocumentType.FromValue("NonExistent");

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("10-K", "TenK")]
    [InlineData("8-K", "EightK")]
    [InlineData("10-Q", "TenQ")]
    [InlineData("20-F", "TwentyF")]
    [InlineData("4", "FormFour")]
    [InlineData("3", "FormThree")]
    public void FromDisplayName_ReturnsCorrectType(string displayName, string expectedValue) {
        var result = DocumentType.FromDisplayName(displayName);

        result.Should().NotBeNull();
        result.Value.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("10-k", "TenK")]
    [InlineData("8-K/a", "EightKa")]
    public void FromDisplayName_IsCaseInsensitive(string input, string expectedValue) {
        var result = DocumentType.FromDisplayName(input);

        result.Should().NotBeNull();
        result.Value.Should().Be(expectedValue);
    }

    [Fact]
    public void FromDisplayName_UnknownDisplayName_ReturnsNull() {
        var result = DocumentType.FromDisplayName("99-Z");

        result.Should().BeNull();
    }

    [Fact]
    public void GetAll_ContainsAllBuiltInTypes() {
        var all = DocumentType.GetAll().ToList();

        all.Should().Contain(new[] {
            DocumentType.TenK, DocumentType.TenQ, DocumentType.EightK,
            DocumentType.TenKa, DocumentType.TenQa, DocumentType.EightKa,
            DocumentType.TwentyF, DocumentType.SixK, DocumentType.FortyF,
            DocumentType.FormFour, DocumentType.FormThree, DocumentType.Other
        });
    }

    [Fact]
    public void ToString_ReturnsDisplayName() {
        DocumentType.TenK.ToString().Should().Be("10-K");
        DocumentType.EightK.ToString().Should().Be("8-K");
        DocumentType.FormFour.ToString().Should().Be("4");
        DocumentType.Other.ToString().Should().Be("Other");
    }

    [Fact]
    public void Equality_SameValueTypes_AreEqual() {
        var type = DocumentType.FromValue("TenK");

        type.Should().Be(DocumentType.TenK);
        type.Equals(DocumentType.TenK).Should().BeTrue();
        type.GetHashCode().Should().Be(DocumentType.TenK.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentTypes_AreNotEqual() {
        DocumentType.TenK.Should().NotBe(DocumentType.TenQ);
        DocumentType.TenK.Equals(DocumentType.TenQ).Should().BeFalse();
    }

    [Fact]
    public void Equals_NonDocumentTypeObject_ReturnsFalseViaTypeCheck() {
        // Equals is overridden as `obj is DocumentType other && Value == other.Value`.
        // The Value-mismatch branch is pinned by `Equality_DifferentTypes_AreNotEqual`,
        // but the `obj is DocumentType` false leg (where obj is null or a foreign type)
        // wasn't pinned. A refactor that swapped the pattern for a direct
        // `((DocumentType)obj).Value == Value` would NRE on null and InvalidCast on
        // foreign types — both as runtime errors instead of a clean false. Pin the
        // type-check leg so the regression fails at unit-test time.
        DocumentType.TenK.Equals(null).Should().BeFalse();
        DocumentType.TenK.Equals("TenK").Should().BeFalse();
    }

    [Fact]
    #pragma warning disable CS1718 // Intentional: testing custom == and != operators
    public void Operators_EqualityAndInequality_Work() {
        (DocumentType.TenK == DocumentType.TenK).Should().BeTrue();
        (DocumentType.TenK != DocumentType.TenQ).Should().BeTrue();
        (DocumentType.TenK == DocumentType.TenQ).Should().BeFalse();
        (DocumentType.TenK != DocumentType.TenK).Should().BeFalse();
    }
    #pragma warning restore CS1718

    [Fact]
    public void Register_AddsNewType_FoundByFromValueAndFromDisplayName() {
        var custom = new DocumentType("CustomFiling", "CUSTOM-1");

        DocumentType.Register(custom);

        DocumentType.FromValue("CustomFiling").Should().Be(custom);
        DocumentType.FromDisplayName("CUSTOM-1").Should().Be(custom);
    }

    [Fact]
    public void Constructor_OmittedDisplayName_DefaultsToValue() {
        // The ctor's `displayName ?? value` fallback lets callers register a
        // wire-style DocumentType where the human-facing label is the same as
        // the stable internal Value. Every built-in static instance passes an
        // explicit displayName, so the fallback is only exercised via the
        // public single-arg ctor. Pin it so a refactor that drops the
        // `?? value` (e.g. requiring displayName non-null) breaks here
        // rather than producing a DocumentType with a null DisplayName that
        // ToString() and the AllByDisplayName registration would later trip on.
        var custom = new DocumentType("WireOnly");

        custom.DisplayName.Should().Be("WireOnly");
    }
}
