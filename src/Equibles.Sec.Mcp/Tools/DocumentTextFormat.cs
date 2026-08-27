using Equibles.Mcp.Helpers;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Mcp.Tools;

// Line and document-header rendering shared by the document text tools (ReadDocumentLines,
// SearchDocument exact-mode results) and the keyword scan reuse.
internal static class DocumentTextFormat
{
    internal static string Line(int lineNumber, string content)
    {
        return $"{lineNumber, 6} │ {content}";
    }

    internal static string Header(Document document) =>
        $"{document.CommonStock.Name} ({document.CommonStock.Ticker}) {document.DocumentType} filed {McpFormat.Invariant(document.ReportingDate, "yyyy-MM-dd")}";
}
