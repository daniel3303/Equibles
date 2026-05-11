namespace Equibles.Sec.BusinessLogic;

public static class SecDocumentEnvelopeParser {
    private const string DocumentStartTag = "<DOCUMENT>";
    private const string DocumentEndTag = "</DOCUMENT>";

    /// <summary>
    /// Some SEC filings (typically &lt;PAPER&gt; 6-K submissions and other digitized filings) are
    /// stored as an SGML envelope wrapping a single uuencoded PDF rather than HTML. The HTML
    /// normalizer rejects these by filename, leaving no extractable content. This method spots
    /// the PDF so the caller can fetch the standalone PDF artifact directly from
    /// /Archives/edgar/data/{cik}/{accession}/{filename}.
    /// </summary>
    /// <returns>True when a PDF filename was found; the filename is written to <paramref name="filename"/>.</returns>
    public static bool TryExtractPaperPdfFilename(string envelope, out string filename) {
        filename = string.Empty;
        if (string.IsNullOrEmpty(envelope)) return false;

        var pos = 0;
        while (pos < envelope.Length) {
            var blockStart = envelope.IndexOf(DocumentStartTag, pos, StringComparison.OrdinalIgnoreCase);
            if (blockStart == -1) return false;

            var blockEnd = envelope.IndexOf(DocumentEndTag, blockStart, StringComparison.OrdinalIgnoreCase);
            if (blockEnd == -1) return false;

            var block = envelope.Substring(blockStart, blockEnd - blockStart + DocumentEndTag.Length);
            pos = blockEnd + DocumentEndTag.Length;

            if (!TryExtractSgmlTagValue(block, "FILENAME", out var candidate)) continue;
            if (!candidate.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsSafeFilename(candidate)) continue;

            filename = candidate;
            return true;
        }

        return false;
    }

    // Filename flows from an untrusted envelope body into a URL. EDGAR filenames are always
    // bare names ([A-Za-z0-9._-]+); reject anything that could traverse paths or escape the
    // expected directory even though the host is already locked to www.sec.gov.
    private static bool IsSafeFilename(string value) {
        if (value.Length == 0) return false;
        if (value[0] == '.') return false;
        foreach (var ch in value) {
            if (ch == '/' || ch == '\\') return false;
        }
        return true;
    }

    private static bool TryExtractSgmlTagValue(string block, string tagName, out string value) {
        value = string.Empty;
        var tagMarker = $"<{tagName}>";
        var idx = block.IndexOf(tagMarker, StringComparison.OrdinalIgnoreCase);
        if (idx == -1) return false;

        var valueStart = idx + tagMarker.Length;
        var end = valueStart;
        while (end < block.Length && block[end] != '\n' && block[end] != '\r' && block[end] != '<') {
            end++;
        }

        var raw = block.Substring(valueStart, end - valueStart).Trim();
        if (raw.Length == 0) return false;

        value = raw.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0];
        return true;
    }
}
