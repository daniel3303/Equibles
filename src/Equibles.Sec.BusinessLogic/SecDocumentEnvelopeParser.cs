using Equibles.Sec.Data.Models;

namespace Equibles.Sec.BusinessLogic;

public static class SecDocumentEnvelopeParser
{
    private const string DocumentStartTag = "<DOCUMENT>";
    private const string DocumentEndTag = "</DOCUMENT>";
    private const string TextStartTag = "<TEXT>";
    private const string TextEndTag = "</TEXT>";

    /// <summary>
    /// Some SEC filings (typically &lt;PAPER&gt; 6-K submissions and other digitized filings) are
    /// stored as an SGML envelope wrapping a single uuencoded PDF rather than HTML. The HTML
    /// normalizer rejects these by filename, leaving no extractable content. This method spots
    /// the PDF so the caller can fetch the standalone PDF artifact directly from
    /// /Archives/edgar/data/{cik}/{accession}/{filename}.
    /// </summary>
    /// <returns>True when a PDF filename was found; the filename is written to <paramref name="filename"/>.</returns>
    public static bool TryExtractPaperPdfFilename(string envelope, out string filename)
    {
        filename = string.Empty;

        foreach (var block in EnumerateDocumentBlocks(envelope))
        {
            if (!TryExtractSgmlTagValue(block, "FILENAME", out var candidate))
                continue;
            if (!candidate.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!IsSafeFilename(candidate))
                continue;

            filename = candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Extracts the raw XBRL envelope from a full SEC submission (the <c>{accession}.txt</c>
    /// file). Prefers the inline iXBRL primary document; when the primary carries no inline
    /// XBRL it falls back to the standalone XBRL instance — the <c>&lt;DOCUMENT&gt;</c> whose
    /// SGML <c>&lt;TYPE&gt;</c> ends in <c>.INS</c> (e.g. <c>EX-101.INS</c>). Reading the
    /// envelope already fetched for ingest means no extra EDGAR round-trip.
    /// </summary>
    /// <returns>True when an XBRL envelope was found; otherwise the filing carries no XBRL.</returns>
    public static bool TryExtractXbrlEnvelope(
        string envelope,
        string primaryDocumentFileName,
        out XbrlType type,
        out string sourceFileName,
        out string content
    )
    {
        type = default;
        sourceFileName = string.Empty;
        content = string.Empty;

        if (string.IsNullOrEmpty(envelope))
            return false;

        string standaloneFileName = null;
        string standaloneBody = null;

        foreach (var block in EnumerateDocumentBlocks(envelope))
        {
            TryExtractSgmlTagValue(block, "FILENAME", out var blockFileName);
            TryExtractSgmlTagValue(block, "TYPE", out var blockType);

            // Inline iXBRL is embedded in the primary document; prefer it and return as
            // soon as we confirm the primary block actually carries inline markers.
            if (
                !string.IsNullOrEmpty(primaryDocumentFileName)
                && string.Equals(
                    blockFileName,
                    primaryDocumentFileName,
                    StringComparison.OrdinalIgnoreCase
                )
                && TryExtractTextBody(block, out var primaryBody)
                && ContainsInlineXbrl(primaryBody)
            )
            {
                type = XbrlType.InlineIxbrl;
                sourceFileName = blockFileName;
                content = primaryBody;
                return true;
            }

            // Older filings ship the instance as a separate EX-10x.INS document. Remember the
            // first one in case the primary turns out to have no inline XBRL.
            if (
                standaloneBody == null
                && !string.IsNullOrEmpty(blockType)
                && blockType.EndsWith(".INS", StringComparison.OrdinalIgnoreCase)
                && TryExtractTextBody(block, out var instanceBody)
            )
            {
                standaloneFileName = blockFileName;
                standaloneBody = instanceBody;
            }
        }

        if (standaloneBody != null)
        {
            type = XbrlType.StandaloneXbrl;
            sourceFileName = standaloneFileName ?? string.Empty;
            content = standaloneBody;
            return true;
        }

        return false;
    }

    /// <summary>
    /// True when the document carries inline XBRL — the <c>ix</c> namespace declaration or
    /// any element in that namespace.
    /// </summary>
    public static bool ContainsInlineXbrl(string content)
    {
        if (string.IsNullOrEmpty(content))
            return false;

        return content.Contains("xmlns:ix=", StringComparison.OrdinalIgnoreCase)
            || content.Contains("<ix:", StringComparison.OrdinalIgnoreCase);
    }

    // Walks the SGML envelope yielding each <DOCUMENT>...</DOCUMENT> block verbatim. Stops at
    // the first unterminated block, matching EDGAR's well-formed-or-nothing guarantee.
    private static IEnumerable<string> EnumerateDocumentBlocks(string envelope)
    {
        if (string.IsNullOrEmpty(envelope))
            yield break;

        var pos = 0;
        while (pos < envelope.Length)
        {
            var blockStart = envelope.IndexOf(
                DocumentStartTag,
                pos,
                StringComparison.OrdinalIgnoreCase
            );
            if (blockStart == -1)
                yield break;

            var blockEnd = envelope.IndexOf(
                DocumentEndTag,
                blockStart,
                StringComparison.OrdinalIgnoreCase
            );
            if (blockEnd == -1)
                yield break;

            yield return envelope.Substring(
                blockStart,
                blockEnd - blockStart + DocumentEndTag.Length
            );
            pos = blockEnd + DocumentEndTag.Length;
        }
    }

    // Returns the document body between <TEXT> and </TEXT>. The body may itself be large
    // (a full iXBRL primary document), so the substring is taken span-wise without copying tags.
    private static bool TryExtractTextBody(string block, out string body)
    {
        body = string.Empty;

        var start = block.IndexOf(TextStartTag, StringComparison.OrdinalIgnoreCase);
        if (start == -1)
            return false;

        var bodyStart = start + TextStartTag.Length;
        var end = block.LastIndexOf(TextEndTag, StringComparison.OrdinalIgnoreCase);
        if (end == -1 || end < bodyStart)
            return false;

        body = block.Substring(bodyStart, end - bodyStart).Trim();
        return body.Length > 0;
    }

    // Filename flows from an untrusted envelope body into a URL. EDGAR filenames are always
    // bare names ([A-Za-z0-9._-]+); reject anything that could traverse paths or escape the
    // expected directory even though the host is already locked to www.sec.gov.
    private static bool IsSafeFilename(string value)
    {
        if (value.Length == 0)
            return false;
        if (value[0] == '.')
            return false;
        // Enforce the bare-name allowlist rather than blocklisting only literal
        // separators: a name containing '%' (URL-encoded '../' = %2e%2e%2f) is
        // not a bare name and the remote server decodes it back to a traversal.
        foreach (var ch in value)
        {
            if (!IsBareNameChar(ch))
                return false;
        }
        return true;
    }

    private static bool IsBareNameChar(char ch)
    {
        return char.IsAsciiLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-';
    }

    private static bool TryExtractSgmlTagValue(string block, string tagName, out string value)
    {
        value = string.Empty;
        var tagMarker = $"<{tagName}>";
        var idx = block.IndexOf(tagMarker, StringComparison.OrdinalIgnoreCase);
        if (idx == -1)
            return false;

        var valueStart = idx + tagMarker.Length;
        var end = valueStart;
        while (end < block.Length && block[end] != '\n' && block[end] != '\r' && block[end] != '<')
        {
            end++;
        }

        var raw = block.Substring(valueStart, end - valueStart).Trim();
        if (raw.Length == 0)
            return false;

        value = raw.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0];
        return true;
    }
}
