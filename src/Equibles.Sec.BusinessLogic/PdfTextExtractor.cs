using System.Text;
using Equibles.Core.AutoWiring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Equibles.Sec.BusinessLogic;

[Service(ServiceLifetime.Scoped, typeof(IPdfTextExtractor))]
public class PdfTextExtractor : IPdfTextExtractor
{
    private readonly ILogger<PdfTextExtractor> _logger;

    public PdfTextExtractor(ILogger<PdfTextExtractor> logger)
    {
        _logger = logger;
    }

    public string Extract(byte[] pdfBytes)
    {
        if (pdfBytes == null || pdfBytes.Length == 0)
            return string.Empty;

        try
        {
            using var document = PdfDocument.Open(pdfBytes);

            // One entry a page with a form feed between them, empty pages included, so page N of
            // the text is page N of the file.
            var result = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                if (result.Length > 0 || page.Number > 1)
                    result.Append('\f');
                result.Append(PageText(page));
            }

            // A file with no text on any page is whitespace only, which callers read as no text.
            return result.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            // Encrypted, malformed, or otherwise unreadable PDF — treat as no extractable content.
            // The caller logs a follow-up warning identifying the filing.
            _logger.LogWarning(ex, "PdfPig could not read PDF ({Bytes} bytes)", pdfBytes.Length);
            return string.Empty;
        }
    }

    // The page's words in reading order, with the spaces and line ends their places on the page
    // imply. Page.Text is only the letters the file stores, and a typeset report stores no space
    // between its words, so it comes out as one run of letters.
    private static string PageText(Page page)
    {
        try
        {
            return ContentOrderTextExtractor.GetText(page).Replace("\r", string.Empty).TrimEnd();
        }
        catch (Exception)
        {
            return page.Text ?? string.Empty;
        }
    }
}
