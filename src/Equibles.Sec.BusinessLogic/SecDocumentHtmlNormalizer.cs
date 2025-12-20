using System.Text;
using AngleSharp.Html.Parser;
using Equibles.Core.AutoWiring;
using Equibles.Sec.Data.Models;
using Equibles.Sec.BusinessLogic.Normalizers;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Sec.BusinessLogic;

[Service(ServiceLifetime.Scoped, typeof(ISecDocumentHtmlNormalizer))]
public class SecDocumentHtmlNormalizer : ISecDocumentHtmlNormalizer {
    private readonly HtmlParser _parser = new(new HtmlParserOptions {
        IsAcceptingCustomElementsEverywhere = true
    });

    private readonly List<IHtmlNormalizationStep> _steps;

    public SecDocumentHtmlNormalizer() {
        _steps = [
            new XbrlStripStep(),
            new TableNormalizationStep(_parser),
            new PaginationRemovalStep(),
            new HeadingConversionStep(),
            new ListConversionStep(),
            new CurrencyConsolidationStep(),
        ];
    }

    public string Normalize(string html) {
        if (string.IsNullOrWhiteSpace(html)) {
            return string.Empty;
        }

        var filteredHtml = ExtractAndFilterDocuments(html);
        if (string.IsNullOrEmpty(filteredHtml)) {
            return string.Empty;
        }

        var tempDoc = _parser.ParseDocument(filteredHtml);

        foreach (var step in _steps) {
            step.Execute(tempDoc);
        }

        return tempDoc.Body?.InnerHtml ?? string.Empty;
    }

    private bool IsAllowedDocumentType(string documentType) {
        if (DocumentType.FromDisplayName(documentType) != null) {
            return true;
        }

        if (documentType.StartsWith("EX-")) {
            var exNumberPart = documentType.Substring(3);
            var exNumberPartClean = exNumberPart.Split('.')[0];
            if (int.TryParse(exNumberPartClean, out var exNumber) && exNumber < 100) {
                return true;
            }
        }

        return false;
    }

    private string ExtractAndFilterDocuments(string rawText) {
        var finalHtml = new StringBuilder();
        var searchText = rawText;
        var startTag = "<DOCUMENT>";
        var endTag = "</DOCUMENT>";

        var pos = 0;
        while (pos < searchText.Length) {
            var blockStart = searchText.IndexOf(startTag, pos, StringComparison.OrdinalIgnoreCase);
            if (blockStart == -1) break;

            var blockEnd = searchText.IndexOf(endTag, blockStart, StringComparison.OrdinalIgnoreCase);
            if (blockEnd == -1) break;

            var block = searchText.Substring(blockStart, blockEnd - blockStart + endTag.Length);
            pos = blockEnd + endTag.Length;

            var typeText = ExtractSgmlTagValue(block, "TYPE");
            if (string.IsNullOrEmpty(typeText) || !IsAllowedDocumentType(typeText)) continue;

            var filename = ExtractSgmlTagValue(block, "FILENAME");
            if (string.IsNullOrEmpty(filename)) continue;
            if (!filename.EndsWith(".htm") && !filename.EndsWith(".html") && !filename.EndsWith(".txt")) continue;

            var content = ExtractInnerContent(block, "XBRL")
                          ?? ExtractInnerContent(block, "TEXT")
                          ?? block;

            finalHtml.Append(content);
        }

        return finalHtml.ToString();
    }

    private static string ExtractSgmlTagValue(string block, string tagName) {
        var tagMarker = $"<{tagName}>";
        var idx = block.IndexOf(tagMarker, StringComparison.OrdinalIgnoreCase);
        if (idx == -1) return null;

        var valueStart = idx + tagMarker.Length;
        var end = valueStart;
        while (end < block.Length && block[end] != '\n' && block[end] != '\r' && block[end] != '<') {
            end++;
        }

        var raw = block.Substring(valueStart, end - valueStart).Trim();
        if (string.IsNullOrEmpty(raw)) return null;

        return raw.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0];
    }

    private static string ExtractInnerContent(string block, string tagName) {
        var openTag = $"<{tagName}>";
        var closeTag = $"</{tagName}>";

        var openIdx = block.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
        if (openIdx == -1) return null;

        var contentStart = openIdx + openTag.Length;
        var closeIdx = block.IndexOf(closeTag, contentStart, StringComparison.OrdinalIgnoreCase);
        if (closeIdx == -1) return null;

        return block.Substring(contentStart, closeIdx - contentStart);
    }
}
