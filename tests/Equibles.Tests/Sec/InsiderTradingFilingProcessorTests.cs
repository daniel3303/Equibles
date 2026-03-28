using Equibles.InsiderTrading.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Tests.Sec;

public class InsiderTradingFilingProcessorTests {
    // ── SanitizeXml ──

    [Fact]
    public void SanitizeXml_WithSgmlEnvelope_ExtractsInnerXml() {
        var input = """
            <SEC-DOCUMENT>
            <XML>
            <ownershipDocument><root/></ownershipDocument>
            </XML>
            </SEC-DOCUMENT>
            """;

        var result = InsiderTradingFilingProcessor.SanitizeXml(input);

        result.Should().Contain("<ownershipDocument>");
        result.Should().NotContain("<SEC-DOCUMENT>");
        result.Should().NotContain("<XML>");
    }

    [Fact]
    public void SanitizeXml_WithoutEnvelope_ReturnsXmlAsIs() {
        var input = "<ownershipDocument><root/></ownershipDocument>";

        var result = InsiderTradingFilingProcessor.SanitizeXml(input);

        result.Should().Contain("<ownershipDocument>");
    }

    [Fact]
    public void SanitizeXml_UnescapedAmpersands_AreEscaped() {
        var input = "<XML><doc>AT&T Corp & Others</doc></XML>";

        var result = InsiderTradingFilingProcessor.SanitizeXml(input);

        result.Should().Contain("AT&amp;T Corp &amp; Others");
    }

    [Fact]
    public void SanitizeXml_AlreadyEscapedEntities_ArePreserved() {
        var input = "<XML><doc>&amp; &lt; &gt; &quot; &apos; &#123; &#x1F;</doc></XML>";

        var result = InsiderTradingFilingProcessor.SanitizeXml(input);

        result.Should().Contain("&amp;");
        result.Should().Contain("&lt;");
        result.Should().Contain("&gt;");
        result.Should().Contain("&quot;");
        result.Should().Contain("&apos;");
        result.Should().Contain("&#123;");
        result.Should().Contain("&#x1F;");
        // Should not double-escape
        result.Should().NotContain("&amp;amp;");
    }

    // ── ParseTransactionCode ──

    [Theory]
    [InlineData("P", TransactionCode.Purchase)]
    [InlineData("S", TransactionCode.Sale)]
    [InlineData("A", TransactionCode.Award)]
    [InlineData("M", TransactionCode.Conversion)]
    [InlineData("X", TransactionCode.Exercise)]
    [InlineData("F", TransactionCode.TaxPayment)]
    [InlineData("E", TransactionCode.Expiration)]
    [InlineData("G", TransactionCode.Gift)]
    [InlineData("I", TransactionCode.Inheritance)]
    [InlineData("W", TransactionCode.Discretionary)]
    public void ParseTransactionCode_ValidCode_ReturnsCorrectEnum(string code, TransactionCode expected) {
        InsiderTradingFilingProcessor.ParseTransactionCode(code).Should().Be(expected);
    }

    [Theory]
    [InlineData("p", TransactionCode.Purchase)]
    [InlineData("s", TransactionCode.Sale)]
    public void ParseTransactionCode_LowerCase_StillParses(string code, TransactionCode expected) {
        InsiderTradingFilingProcessor.ParseTransactionCode(code).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Z")]
    [InlineData("PURCHASE")]
    public void ParseTransactionCode_InvalidOrNull_ReturnsOther(string code) {
        InsiderTradingFilingProcessor.ParseTransactionCode(code).Should().Be(TransactionCode.Other);
    }

    // ── ParseBool ──

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("yes", false)]
    public void ParseBool_VariousInputs_ReturnsExpected(string input, bool expected) {
        InsiderTradingFilingProcessor.ParseBool(input).Should().Be(expected);
    }

    // ── ParseLong ──

    [Theory]
    [InlineData("1000", 1000L)]
    [InlineData("0", 0L)]
    [InlineData("-500", -500L)]
    public void ParseLong_IntegerValues_ParsesCorrectly(string input, long expected) {
        InsiderTradingFilingProcessor.ParseLong(input).Should().Be(expected);
    }

    [Fact]
    public void ParseLong_DecimalValue_TruncatesToLong() {
        // "1234.5678" can't be parsed as long, falls back to ParseDecimal then casts
        InsiderTradingFilingProcessor.ParseLong("1234.5678").Should().Be(1234L);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public void ParseLong_InvalidOrNull_ReturnsZero(string input) {
        InsiderTradingFilingProcessor.ParseLong(input).Should().Be(0L);
    }

    // ── ParseDecimal ──

    [Theory]
    [InlineData("123.45", 123.45)]
    [InlineData("0", 0)]
    [InlineData("-99.99", -99.99)]
    [InlineData("1,234.56", 1234.56)]
    public void ParseDecimal_ValidInput_ReturnsValue(string input, double expected) {
        InsiderTradingFilingProcessor.ParseDecimal(input).Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public void ParseDecimal_InvalidOrNull_ReturnsZero(string input) {
        InsiderTradingFilingProcessor.ParseDecimal(input).Should().Be(0m);
    }

    // ── CanProcess ──

    [Fact]
    public void CanProcess_FormFour_ReturnsTrue() {
        var processor = CreateProcessor();
        processor.CanProcess(DocumentType.FormFour).Should().BeTrue();
    }

    [Fact]
    public void CanProcess_FormThree_ReturnsTrue() {
        var processor = CreateProcessor();
        processor.CanProcess(DocumentType.FormThree).Should().BeTrue();
    }

    [Fact]
    public void CanProcess_TenK_ReturnsFalse() {
        var processor = CreateProcessor();
        processor.CanProcess(DocumentType.TenK).Should().BeFalse();
    }

    [Fact]
    public void CanProcess_EightK_ReturnsFalse() {
        var processor = CreateProcessor();
        processor.CanProcess(DocumentType.EightK).Should().BeFalse();
    }

    // ErrorReporter not needed — only testing CanProcess and static helpers
    private static InsiderTradingFilingProcessor CreateProcessor() {
        return new InsiderTradingFilingProcessor(
            NSubstitute.Substitute.For<IServiceScopeFactory>(),
            NSubstitute.Substitute.For<ILogger<InsiderTradingFilingProcessor>>(),
            null);
    }
}
