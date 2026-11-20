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

    // A filer that has not listed yet (an IPO registrant) has no ticker, so it reads by name
    // alone rather than with an empty "()".
    internal static string Header(Document document)
    {
        var ticker = document.Issuer.Presentation?.Listing?.Ticker;
        var company = string.IsNullOrEmpty(ticker)
            ? document.Issuer.Name
            : $"{document.Issuer.Name} ({ticker})";
        return $"{company} {document.DocumentType} filed {McpFormat.Invariant(document.ReportingDate, "yyyy-MM-dd")}";
    }
}
