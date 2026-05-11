using System.Text;
using Equibles.Core.AutoWiring;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace Equibles.Sec.BusinessLogic;

[Service]
public class PdfTextExtractor {
    private readonly ILogger<PdfTextExtractor> _logger;

    public PdfTextExtractor(ILogger<PdfTextExtractor> logger) {
        _logger = logger;
    }

    public string Extract(byte[] pdfBytes) {
        if (pdfBytes == null || pdfBytes.Length == 0) return string.Empty;

        try {
            using var document = PdfDocument.Open(pdfBytes);

            var result = new StringBuilder();

            foreach (var page in document.GetPages()) {
                var text = page.Text;
                if (string.IsNullOrWhiteSpace(text)) continue;

                result.AppendLine(text);
                result.AppendLine();
            }

            return result.ToString();
        } catch (Exception ex) {
            // Encrypted, malformed, or otherwise unreadable PDF — treat as no extractable content.
            // The caller logs a follow-up warning identifying the filing.
            _logger.LogWarning(ex, "PdfPig could not read PDF ({Bytes} bytes)", pdfBytes.Length);
            return string.Empty;
        }
    }
}
