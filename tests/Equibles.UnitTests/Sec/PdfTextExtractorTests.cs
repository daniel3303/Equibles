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
    public void Extract_TypesetReportPage_KeepsTheSpacesBetweenItsWords()
    {
        // Page 41 of KB Home's annual report to shareholders (SEC, 2026-03-13): the file stores no
        // space between words, so reading the stored letters alone gave
        // "grossprofitmarginfor2025mainlyreflected...".
        var sut = new PdfTextExtractor(Substitute.For<ILogger<PdfTextExtractor>>());
        var pdf = File.ReadAllBytes(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Sec",
                "annual-report-typeset-page.pdf"
            )
        );

        var result = sut.Extract(pdf);

        result.Should().Contain("gross profit margin for 2025 mainly reflected price reductions");
        result.Should().NotContain("grossprofitmargin");
    }

    [Fact]
    public void Extract_SeveralPages_KeepsEachPageInItsPlaceWithFormFeeds()
    {
        var sut = new PdfTextExtractor(Substitute.For<ILogger<PdfTextExtractor>>());
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(
            UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica
        );
        builder.AddPage(595, 842).AddText("First page", 12, new PdfPoint(50, 750), font);
        // A page of pictures holds no text and still counts.
        builder.AddPage(595, 842);
        builder.AddPage(595, 842).AddText("Third page", 12, new PdfPoint(50, 750), font);

        var result = sut.Extract(builder.Build());

        result.Split('\f').Should().Equal("First page", string.Empty, "Third page");
    }

    [Fact]
    public void Extract_PageWhoseFontTheFileLacks_KeepsTheOtherPagesInTheirPlace()
    {
        // PdfPig throws on a page that shows text in a font its resources do not hold.
        var logger = Substitute.For<ILogger<PdfTextExtractor>>();
        var sut = new PdfTextExtractor(logger);

        var result = sut.Extract(ThreePages(secondPageFont: "/Missing", secondPageText: "Lost"));

        result.Split('\f').Should().Equal("First page", string.Empty, "Third page");
        logger
            .ReceivedCalls()
            .Count(c =>
                c.GetMethodInfo().Name == "Log"
                && c.GetArguments().OfType<LogLevel>().FirstOrDefault() == LogLevel.Warning
            )
            .Should()
            .Be(1);
    }

    [Fact]
    public void Extract_FormFeedInsideAPagesOwnText_DoesNotShiftTheLaterPages()
    {
        var sut = new PdfTextExtractor(Substitute.For<ILogger<PdfTextExtractor>>());

        var result = sut.Extract(ThreePages(secondPageFont: "/F1", secondPageText: "Mid\\014dle"));

        var pages = result.Split('\f');
        pages.Should().HaveCount(3);
        pages[2].Should().Be("Third page");
    }

    // A three-page file written by hand: the builder cannot leave a font out of a page's resources.
    private static byte[] ThreePages(string secondPageFont, string secondPageText)
    {
        string Stream(string font, string text)
        {
            var content = $"BT {font} 12 Tf 50 750 Td ({text}) Tj ET";
            return $"<< /Length {content.Length} >>\nstream\n{content}\nendstream";
        }

        string Page(int contents) =>
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 9 0 R >> >> /Contents {contents} 0 R >>";

        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R] /Count 3 >>",
            Page(6),
            Page(7),
            Page(8),
            Stream("/F1", "First page"),
            Stream(secondPageFont, secondPageText),
            Stream("/F1", "Third page"),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        var pdf = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append(
            $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"
        );
        return System.Text.Encoding.ASCII.GetBytes(pdf.ToString());
    }

    [Fact]
    public void Extract_PdfWithNoTextOnAnyPage_ReturnsWhitespaceOnly()
    {
        // The scraper stores such a file as an original without text.
        var sut = new PdfTextExtractor(Substitute.For<ILogger<PdfTextExtractor>>());
        var builder = new PdfDocumentBuilder();
        builder.AddPage(595, 842);
        builder.AddPage(595, 842);

        sut.Extract(builder.Build()).Should().BeNullOrWhiteSpace();
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
