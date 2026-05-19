using Equibles.Errors.Data.Models;

namespace Equibles.UnitTests.Errors;

public class ErrorSourceTests
{
    [Theory]
    [InlineData(nameof(ErrorSource.McpTool), "McpTool")]
    [InlineData(nameof(ErrorSource.DocumentScraper), "DocumentScraper")]
    [InlineData(nameof(ErrorSource.HoldingsScraper), "HoldingsScraper")]
    [InlineData(nameof(ErrorSource.FinraScraper), "FinraScraper")]
    [InlineData(nameof(ErrorSource.FtdScraper), "FtdScraper")]
    [InlineData(nameof(ErrorSource.FinancialFactsScraper), "FinancialFactsScraper")]
    [InlineData(nameof(ErrorSource.DocumentProcessor), "DocumentProcessor")]
    [InlineData(nameof(ErrorSource.CongressScraper), "CongressScraper")]
    [InlineData(nameof(ErrorSource.FredScraper), "FredScraper")]
    [InlineData(nameof(ErrorSource.YahooPriceScraper), "YahooPriceScraper")]
    [InlineData(nameof(ErrorSource.CftcScraper), "CftcScraper")]
    [InlineData(nameof(ErrorSource.CboeScraper), "CboeScraper")]
    [InlineData(nameof(ErrorSource.TranscriptScraper), "TranscriptScraper")]
    [InlineData(nameof(ErrorSource.Other), "Other")]
    public void StaticInstance_HasCorrectValue(string fieldName, string expectedValue)
    {
        var instance = GetStaticInstance(fieldName);
        instance.Value.Should().Be(expectedValue);
    }

    [Fact]
    public void GetAll_ReturnsExactlyTheDeclaredStaticInstances()
    {
        // Reflection-driven so adding a new ErrorSource member can never leave
        // GetAll() silently incomplete (or this test stale on a hard-coded
        // count) — the previous brittle HaveCount(13) broke when
        // FinancialFactsScraper was added.
        var declared = typeof(ErrorSource)
            .GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
            )
            .Where(f => f.FieldType == typeof(ErrorSource))
            .Select(f => (ErrorSource)f.GetValue(null));

        ErrorSource.GetAll().Should().BeEquivalentTo(declared);
    }

    [Theory]
    [InlineData("McpTool")]
    [InlineData("DocumentScraper")]
    [InlineData("Other")]
    public void ToString_ReturnsValue(string value)
    {
        var source = new ErrorSource(value);
        source.ToString().Should().Be(value);
    }

    [Fact]
    public void Equals_SameValue_ReturnsTrue()
    {
        var a = new ErrorSource("McpTool");
        var b = new ErrorSource("McpTool");

        a.Equals(b).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentValue_ReturnsFalse()
    {
        var a = new ErrorSource("McpTool");
        var b = new ErrorSource("Other");

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var source = new ErrorSource("McpTool");

        source.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentType_ReturnsFalse()
    {
        var source = new ErrorSource("McpTool");

        source.Equals("McpTool").Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_SameValue_ReturnsSameHash()
    {
        var a = new ErrorSource("McpTool");
        var b = new ErrorSource("McpTool");

        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void TwoInstances_WithSameValueString_AreEqual()
    {
        var custom = new ErrorSource("McpTool");

        custom.Should().Be(ErrorSource.McpTool);
    }

    private static ErrorSource GetStaticInstance(string fieldName)
    {
        var field = typeof(ErrorSource).GetField(
            fieldName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
        );

        return field?.GetValue(null) as ErrorSource
            ?? throw new ArgumentException($"No static field '{fieldName}' found on ErrorSource");
    }
}
