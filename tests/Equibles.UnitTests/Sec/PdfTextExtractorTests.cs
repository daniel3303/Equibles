using Equibles.Sec.BusinessLogic;
using Microsoft.Extensions.Logging;
using NSubstitute;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace Equibles.UnitTests.Sec;

public class PdfTextExtractorTests
{
    [Fact]
    public void Extract_ValidPdfWithText_ReturnsExtractedTextContainingEmbeddedString()
    {
        // Pin the happy path. Every existing test in this file targets an early-
        // return branch:
        //   • Extract_NullBytes_ReturnsEmptyWithoutLogging       — null guard
        //   • Extract_EmptyBytes_ReturnsEmptyWithoutLogging      — empty guard
        //   • Extract_MalformedBytes_ReturnsEmptyAndLogsWarning  — catch handler
        // The entire SUCCESS path inside the try block — the foreach loop over
        // document.GetPages(), the IsNullOrWhiteSpace guard, the dual
        // result.AppendLine calls, and the final result.ToString() — is unpinned.
        // Production-wise this success path is THE point of PdfTextExtractor:
        // DocumentScraper's paper-PDF fallback feeds Extract a valid PDF
        // recovered from a SEC paper filing envelope (see CreateDocument in
        // DocumentScraper.cs), and the returned text becomes the persisted
        // document body. If Extract silently returns "" on a valid PDF, every
        // paper filing (older 6-K / 20-F documents) skips with "no text"
        // warnings and the user sees no document for the filing.
        //
        // The risk this pin catches: a refactor that compiles cleanly and
        // passes all three existing pins (null/empty/malformed) yet silently
        // returns empty on valid input. Realistic refactor patterns:
        //   • Drop `result.AppendLine(text)` while keeping the blank
        //     `result.AppendLine()` — every page contributes only a blank
        //     line to the output. result.ToString() becomes a string of
        //     newlines, not text content.
        //   • Swap `page.Text` for `page.Number.ToString()` or
        //     `page.PageNumber.ToString()` — typo while "modernizing" property
        //     names. Output is "1\n\n2\n\n..." instead of page text.
        //   • Change `return result.ToString()` to `return string.Empty` —
        //     accidental "let me unify both return paths" simplification under
        //     the false intuition that the catch returns "" so the try should
        //     too (the whole point of the try is that the SUCCESS path returns
        //     the BUILT string).
        //   • Move the IsNullOrWhiteSpace check to skip the entire return
        //     (e.g. `if (string.IsNullOrWhiteSpace(text)) return string.Empty;`
        //     instead of `continue`) — a single blank page short-circuits
        //     every multi-page filing.
        // None of these would fail any existing pin. This test fails on every
        // one of them.
        //
        // Construction: build a one-page PDF in-test via PdfPig's
        // PdfDocumentBuilder (the SAME library Extract uses to read), embed
        // a distinctive sentinel string ("PdfTextExtractor happy path sentinel"
        // — chosen to be non-formatted, ASCII-only, and distinct from anything
        // the production code might emit), feed the bytes back to Extract,
        // and assert the sentinel appears in the returned output. Using the
        // same library for write+read avoids cross-library format-compatibility
        // flakes while still exercising the full PdfDocument.Open →
        // GetPages → page.Text → AppendLine → ToString chain.
        //
        // Helvetica is the canonical Standard14 font and the default PdfPig
        // serializes without external font dependencies. PageSize.A4 is also
        // the writer's default — matches how SEC paper filings ship.
        // PdfPoint(50, 750) places the text in the upper-left, well inside
        // any standard PDF reader's text extraction bounds.
        //
        // Two assertions:
        //   1. The sentinel string appears in the output — proves the foreach
        //      iterated a page, AppendLine wrote the page's text, and
        //      ToString returned the accumulated buffer (not "").
        //   2. No Warning was logged — proves the success path didn't fall
        //      into the catch handler. A regression that threw inside the
        //      try (e.g. NRE on a refactored page.Text accessor) would log a
        //      warning AND return empty, and the first assertion alone
        //      wouldn't distinguish that from the silent-empty refactor.
        var logger = Substitute.For<ILogger<PdfTextExtractor>>();
        var sut = new PdfTextExtractor(logger);

        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(595, 842);
        var font = builder.AddStandard14Font(
            UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica
        );
        page.AddText("PdfTextExtractor happy path sentinel", 12, new PdfPoint(50, 750), font);
        var pdfBytes = builder.Build();

        var result = sut.Extract(pdfBytes);

        result.Should().Contain("PdfTextExtractor happy path sentinel");
        logger
            .ReceivedCalls()
            .Any(c =>
                c.GetMethodInfo().Name == "Log"
                && c.GetArguments().OfType<LogLevel>().FirstOrDefault() == LogLevel.Warning
            )
            .Should()
            .BeFalse("a successful extraction must not log a warning");
    }

    [Fact]
    public void Extract_MalformedBytes_ReturnsEmptyAndLogsWarning()
    {
        var logger = Substitute.For<ILogger<PdfTextExtractor>>();
        var sut = new PdfTextExtractor(logger);
        var notAPdf = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }; // "GIF89a" magic

        var result = sut.Extract(notAPdf);

        result.Should().BeEmpty();
        logger
            .ReceivedCalls()
            .Any(c =>
                c.GetMethodInfo().Name == "Log"
                && c.GetArguments().OfType<LogLevel>().FirstOrDefault() == LogLevel.Warning
            )
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Extract_EmptyBytes_ReturnsEmptyWithoutLogging()
    {
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
        logger.ReceivedCalls().Any(c => c.GetMethodInfo().Name == "Log").Should().BeFalse();
    }

    [Fact]
    public void Extract_NullBytes_ReturnsEmptyWithoutLogging()
    {
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
        logger.ReceivedCalls().Any(c => c.GetMethodInfo().Name == "Log").Should().BeFalse();
    }
}
