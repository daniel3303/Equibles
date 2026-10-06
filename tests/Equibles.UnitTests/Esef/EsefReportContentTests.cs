using System.IO.Compression;
using System.Text;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.HostedService.Services;
using FluentAssertions;

namespace Equibles.UnitTests.Esef;

// Against a real report's own markup; see TestAssets/Esef/README.md for what was cut and what was not.
public class EsefReportContentTests
{
    private static string Report() =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Esef", "izs-2022-excerpt.xhtml")
        );

    private static string BuildText(string html) =>
        Encoding.UTF8.GetString(
            EsefReportContent.Build(
                html,
                new SecDocumentHtmlNormalizer(),
                new SecDocumentHtmlToMarkdownConverter()
            )
        );

    [Fact]
    public void Build_RealNoteWithRootHeading_PreservesPayablesTable()
    {
        using var file = File.OpenRead(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Esef",
                "syncona-2026-payables-note.html.gz"
            )
        );
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var markup = reader.ReadToEnd();

        var text = BuildText(markup);

        text.Should().Contain("13. ACCRUED EXPENSES AND PAYABLES");
        text.Should()
            .Contain("Charitable donations payable")
            .And.Contain("3,642")
            .And.Contain("4,002");
        text.Should().Contain("Management fees accrued").And.Contain("2,951").And.Contain("1,079");
        text.Should().Contain("Other payables").And.Contain("762").And.Contain("1,182");
        text.Should().Contain("Total").And.Contain("7,355").And.Contain("6,263");
        text.Should().Contain("2026").And.Contain("2025").And.Contain("£’000");
    }

    [Fact]
    public void StripEmbeddedData_RemovesAnImageAndAFontRulePayload()
    {
        var html = Report();
        html.Should().Contain("data:image/png;base64,");
        html.Should().Contain("data:font/truetype");

        var stripped = EsefReportContent.StripEmbeddedData(html);

        stripped.Should().NotContain("base64,");
        stripped.Should().Contain("<img src=\"\"");
        stripped.Should().Contain("src: url()");
        stripped.Length.Should().BeLessThan(html.Length);
    }

    [Fact]
    public void StripEmbeddedData_KeepsAQuotedPhraseThatOnlyOpensWithTheWord()
    {
        EsefReportContent.StripEmbeddedData(Report()).Should().Contain("\"data: 31.12.2022\"");
    }

    [Fact]
    public void Build_ReadsTheBalanceSheetOutOfABareReport()
    {
        var text = BuildText(Report());

        // The figures the retrieval body exists for, with their labels and both comparative columns.
        text.Should().Contain("| A. Aktywa trwałe (długoterminowe) |  | 209 259 | 190 567 |");
        text.Should().Contain("| Aktywa razem |  | 693 232 | 720 184 |");
        text.Should().NotContain("base64");
        // The contexts and units the extractor reads out of the envelope are not retrieval text.
        text.Should().NotContain("xbrli:identifier");
    }

    [Fact]
    public void Build_PastTheRetrievalCeiling_ReturnsNoBodyRatherThanParsingIt()
    {
        var filler = new string('a', EsefReportContent.MaxRetrievalHtmlChars + 1);

        EsefReportContent
            .Build(
                $"<html><body><p>{filler}</p></body></html>",
                new SecDocumentHtmlNormalizer(),
                new SecDocumentHtmlToMarkdownConverter()
            )
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Build_OnAnEnvelopeWrappedFiling_IsWhatTheSecLaneWouldRead()
    {
        // NormalizeFragment is the only difference from the SEC lane: Normalize selects the allowed forms
        // out of a submission's SGML blocks, and a report that arrives on its own has none, so it would
        // yield nothing at all.
        new SecDocumentHtmlNormalizer()
            .Normalize(Report())
            .Should()
            .BeEmpty();
        new SecDocumentHtmlNormalizer().NormalizeFragment(Report()).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_OnNothing_IsEmpty(string html)
    {
        BuildText(html).Should().BeEmpty();
    }
}
