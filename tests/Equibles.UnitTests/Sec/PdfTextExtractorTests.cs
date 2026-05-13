using Equibles.Sec.BusinessLogic;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class PdfTextExtractorTests {
    [Fact]
    public void Extract_MalformedBytes_ReturnsEmptyAndLogsWarning() {
        var logger = Substitute.For<ILogger<PdfTextExtractor>>();
        var sut = new PdfTextExtractor(logger);
        var notAPdf = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }; // "GIF89a" magic

        var result = sut.Extract(notAPdf);

        result.Should().BeEmpty();
        logger.ReceivedCalls()
            .Any(c => c.GetMethodInfo().Name == "Log"
                && c.GetArguments().OfType<LogLevel>().FirstOrDefault() == LogLevel.Warning)
            .Should().BeTrue();
    }

    [Fact]
    public void Extract_EmptyBytes_ReturnsEmptyWithoutLogging() {
        // The guard at the top of Extract short-circuits on BOTH null and
        // Length == 0. Without the Length == 0 branch, an empty byte[] would
        // reach PdfPig, throw, hit the catch handler, and log a warning on
        // every empty filing artifact. Pin the silent early-return so a
        // refactor that simplifies the guard to `pdfBytes == null` surfaces
        // as log spam in the warning-count assertion below.
        var logger = Substitute.For<ILogger<PdfTextExtractor>>();
        var sut = new PdfTextExtractor(logger);

        var result = sut.Extract([]);

        result.Should().BeEmpty();
        logger.ReceivedCalls()
            .Any(c => c.GetMethodInfo().Name == "Log")
            .Should().BeFalse();
    }

    [Fact]
    public void Extract_NullBytes_ReturnsEmptyWithoutLogging() {
        // Extract is called from the SEC paper-PDF fallback in DocumentScraper:
        // when SecDocumentEnvelopeParser locates a PDF filename but the artifact
        // fetch returns no bytes, the caller may hand us a null. The early
        // null/empty short-circuit MUST run *before* PdfPig touches anything,
        // otherwise we hit an NRE or log a confusing PdfPig warning. Pin the
        // null path so a refactor that drops the guard surfaces immediately —
        // the malformed-bytes [Fact] only exercises the catch handler.
        var logger = Substitute.For<ILogger<PdfTextExtractor>>();
        var sut = new PdfTextExtractor(logger);

        var result = sut.Extract(null);

        result.Should().BeEmpty();
        logger.ReceivedCalls()
            .Any(c => c.GetMethodInfo().Name == "Log")
            .Should().BeFalse();
    }
}
