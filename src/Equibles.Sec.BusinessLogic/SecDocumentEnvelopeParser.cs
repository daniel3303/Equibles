using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.BusinessLogic;

public static class SecDocumentEnvelopeParser
{
    // Cap on a surfaced image filename, matching the DocumentImage.FileName column. The stitcher
    // skips longer names so the stored lookup key always equals the name left in the as-filed HTML.
    private const int MaxImageFileNameLength = 256;

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

        string inlineFallbackFileName = null;
        string inlineFallbackBody = null;
        string standaloneFileName = null;
        string standaloneBody = null;

        foreach (var block in EnumerateDocumentBlocks(envelope))
        {
            TryExtractSgmlTagValue(block, "FILENAME", out var blockFileName);
            TryExtractSgmlTagValue(block, "TYPE", out var blockType);

            // Inline iXBRL is embedded in the primary document; prefer the named primary and
            // return as soon as we confirm it actually carries inline markers.
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

            // Fallback inline: any block whose body carries inline markers, covering filings
            // whose PrimaryDocument name is missing or doesn't match the envelope's FILENAME —
            // without it those would be wrongly recorded as NotPresent (terminal). The cheap
            // pre-check on the raw block avoids materializing bodies for non-XBRL exhibits.
            if (
                inlineFallbackBody == null
                && ContainsInlineXbrl(block)
                && TryExtractTextBody(block, out var fallbackBody)
            )
            {
                inlineFallbackFileName = blockFileName;
                inlineFallbackBody = fallbackBody;
            }

            // Older filings ship the instance as a separate EX-10x.INS document. Remember the
            // first one in case neither the named primary nor any block carries inline XBRL.
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

        if (inlineFallbackBody != null)
        {
            type = XbrlType.InlineIxbrl;
            sourceFileName = inlineFallbackFileName ?? string.Empty;
            content = inlineFallbackBody;
            return true;
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

    private static bool TryExtractSgmlTagValue(string block, string tagName, out string value) =>
        SecSgmlEnvelope.TryGetTagValue(block, tagName, out value);

    /// <summary>
    /// Builds a single "as-filed" HTML page from a full SEC submission envelope: the primary
    /// document followed by each displayable exhibit (a registered form or <c>EX-* &lt; 100</c>),
    /// each wrapped in an anchored <c>&lt;section&gt;</c>, with intra-filing links to those
    /// exhibits rewritten to in-page anchors. This lets the document viewer show the WHOLE
    /// filing — e.g. an 8-K cover page PLUS its Exhibit 99.1 press release — so a citation
    /// grounded in an exhibit resolves on the page and the cover page's exhibit links scroll
    /// in-page instead of dead-ending on a file we don't host. Reads only the in-hand submission,
    /// so it costs no extra EDGAR round-trip.
    /// </summary>
    /// <returns>
    /// True and the stitched HTML when the filing carries at least one displayable exhibit;
    /// false when there's nothing to stitch (single document, or no displayable exhibit).
    /// </returns>
    public static bool TryBuildAsFiledHtml(
        string envelope,
        string primaryDocumentFileName,
        out string content
    ) => TryBuildAsFiledHtml(envelope, primaryDocumentFileName, out content, out _);

    /// <summary>
    /// As <see cref="TryBuildAsFiledHtml(string,string,out string)"/>, but also surfaces the bare
    /// relative filenames of the images the stitched page references (e.g. an 8-K investor-deck's
    /// slide JPGs and the cover-page logo). Each such <c>&lt;img src&gt;</c> is normalized in the
    /// stored HTML to its bare filename so the viewer can rewrite it to a same-origin proxy by
    /// (document, filename); the caller downloads these from EDGAR and stores them. Inline
    /// <c>data:</c> images and non-image references are left untouched and not surfaced.
    /// </summary>
    public static bool TryBuildAsFiledHtml(
        string envelope,
        string primaryDocumentFileName,
        out string content,
        out IReadOnlyList<string> imageFileNames
    )
    {
        content = string.Empty;
        imageFileNames = [];
        if (string.IsNullOrEmpty(envelope))
            return false;

        var docs = new List<AsFiledBlock>();
        foreach (var block in EnumerateDocumentBlocks(envelope))
        {
            if (
                !TryExtractSgmlTagValue(block, "TYPE", out var blockType)
                || string.IsNullOrEmpty(blockType)
                || !IsDisplayableDocumentType(blockType)
            )
                continue;

            TryExtractSgmlTagValue(block, "FILENAME", out var blockFileName);
            if (!IsHtmlFilename(blockFileName))
                continue;
            if (!TryExtractTextBody(block, out var body))
                continue;

            var isPrimary =
                !string.IsNullOrEmpty(primaryDocumentFileName)
                && string.Equals(
                    blockFileName,
                    primaryDocumentFileName,
                    StringComparison.OrdinalIgnoreCase
                );
            var isExhibit = blockType.StartsWith("EX-", StringComparison.OrdinalIgnoreCase);
            docs.Add(new AsFiledBlock(blockFileName, blockType, body, isPrimary, isExhibit));
        }

        // Nothing to stitch unless there's a primary AND at least one exhibit to fold in.
        if (docs.Count < 2 || !docs.Exists(d => d.IsExhibit))
            return false;

        // EDGAR lists the primary document first; if none was flagged (e.g. a backfill that
        // doesn't know the primary's filename), treat the first displayable block as primary.
        if (!docs.Exists(d => d.IsPrimary))
            docs[0].IsPrimary = true;

        // Primary leads, exhibits follow in envelope order. Assign stable section ids and map
        // each filename to its section so links into the filing can target it.
        docs = docs.OrderByDescending(d => d.IsPrimary).ToList();
        var sectionByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < docs.Count; i++)
        {
            docs[i].SectionId = $"asfiled-{i}";
            if (!string.IsNullOrEmpty(docs[i].FileName))
                sectionByFile[docs[i].FileName] = docs[i].SectionId;
        }

        var parser = new HtmlParser(
            new HtmlParserOptions { IsAcceptingCustomElementsEverywhere = true }
        );
        var head = new StringBuilder();
        var sections = new StringBuilder();
        var images = new List<string>();
        var seenImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var doc in docs)
        {
            var parsed = parser.ParseDocument(doc.Body);

            // Most SEC formatting is inline style attributes (kept on the elements when we take
            // the body inner HTML); hoist any explicit <head><style> blocks so they survive too.
            // The stitched documents share one cascade, so an exhibit's styles can in principle
            // restyle the cover-page section — acceptable inside the script-free sandboxed frame
            // this is served into (display only; no script/network via the page's CSP).
            if (parsed.Head != null)
            {
                foreach (var style in parsed.Head.QuerySelectorAll("style"))
                    head.Append(style.OuterHtml);
            }

            RewriteIntraFilingLinks(parsed, sectionByFile);
            CollectAndNormalizeImages(parsed, images, seenImages);

            sections.Append("<section id=\"").Append(doc.SectionId).Append('"');
            sections.Append(" data-asfiled-type=\"").Append(Escape(doc.Type)).Append('"');
            sections.Append(" data-asfiled-file=\"").Append(Escape(doc.FileName)).Append("\">");
            sections.Append(parsed.Body?.InnerHtml ?? string.Empty);
            sections.Append("</section>");
        }

        content =
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\">"
            + head
            + "</head><body>"
            + sections
            + "</body></html>";
        imageFileNames = images;
        return true;
    }

    // Surfaces the images the page references and rewrites each such <img src> to the bare EDGAR
    // filename. A filing references its images by a relative name pointing at the SEC submission
    // folder (e.g. "ebs2026-03x31deck001.jpg"); normalizing to the bare name lets the viewer match
    // an image to its stored blob by (document, filename) however the filing wrote the src. Inline
    // data: images are left untouched (the viewer serves those directly); anything that isn't a
    // safe bare image filename is left as-is and not surfaced (the viewer drops the broken hotlink).
    private static void CollectAndNormalizeImages(
        IDocument document,
        List<string> imageFileNames,
        HashSet<string> seen
    )
    {
        foreach (var img in document.QuerySelectorAll("img[src]"))
        {
            var src = img.GetAttribute("src");
            if (
                string.IsNullOrEmpty(src)
                || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            )
                continue;

            var fileName = HrefFileName(src);
            if (
                fileName == null
                || fileName.Length > MaxImageFileNameLength
                || !IsSafeFilename(fileName)
                || !IsImageFilename(fileName)
            )
                continue;

            img.SetAttribute("src", fileName);
            if (seen.Add(fileName))
                imageFileNames.Add(fileName);
        }
    }

    private static bool IsImageFilename(string filename) =>
        filename.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || filename.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        || filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        || filename.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

    // Rewrites every intra-filing anchor — one whose href resolves to a filename we kept as a
    // section — to the in-page fragment for that section, so the cover page's "Exhibit 99.1"
    // link scrolls to the stitched exhibit instead of pointing at a file we don't host.
    private static void RewriteIntraFilingLinks(
        IDocument document,
        IReadOnlyDictionary<string, string> sectionByFile
    )
    {
        foreach (var anchor in document.QuerySelectorAll("a[href]"))
        {
            var href = anchor.GetAttribute("href");
            if (string.IsNullOrEmpty(href) || href.StartsWith('#'))
                continue;

            var fileName = HrefFileName(href);
            if (fileName != null && sectionByFile.TryGetValue(fileName, out var section))
                anchor.SetAttribute("href", "#" + section);
        }
    }

    // The bare filename an href points at: drop any query/fragment, take the last path segment.
    // Handles a bare relative name ("ex99-1.htm"), a relative path ("./ex99-1.htm") and an
    // absolute EDGAR URL that self-references the same filing.
    private static string HrefFileName(string href)
    {
        var cut = href.IndexOfAny(['?', '#']);
        if (cut >= 0)
            href = href.Substring(0, cut);

        var slash = href.LastIndexOf('/');
        var name = slash >= 0 ? href.Substring(slash + 1) : href;
        return string.IsNullOrEmpty(name) ? null : name;
    }

    // Whether an SGML <TYPE> line names a document we render in the as-filed view: a registered
    // form (the primary) or a numbered exhibit below 100 (EX-99.1 etc.). Mirrors the allow rule
    // the markdown normalizer uses so the two representations cover the same documents.
    private static bool IsDisplayableDocumentType(string documentTypeLine)
    {
        var tokens = documentTypeLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var take = tokens.Length; take >= 1; take--)
        {
            if (DocumentType.FromDisplayName(string.Join(' ', tokens[..take])) != null)
                return true;
        }

        var documentType = tokens.Length > 0 ? tokens[0] : documentTypeLine;
        if (documentType.StartsWith("EX-", StringComparison.OrdinalIgnoreCase))
        {
            var exNumber = documentType.Substring(3).Split('.')[0];
            if (int.TryParse(exNumber, out var number) && number < 100)
                return true;
        }

        return false;
    }

    private static bool IsHtmlFilename(string filename) =>
        !string.IsNullOrEmpty(filename)
        && (
            filename.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)
            || filename.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
        );

    // Escaping for a value (an untrusted SGML FILENAME/TYPE) placed inside a double-quoted HTML
    // attribute. Escaping the quote is what prevents attribute breakout; & < > are escaped too so
    // the value can't start a tag or entity.
    private static string Escape(string value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value
                .Replace("&", "&amp;")
                .Replace("\"", "&quot;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");

    // One displayable document inside a filing while it's being stitched.
    private sealed class AsFiledBlock(
        string fileName,
        string type,
        string body,
        bool isPrimary,
        bool isExhibit
    )
    {
        public string FileName { get; } = fileName;
        public string Type { get; } = type;
        public string Body { get; } = body;
        public bool IsPrimary { get; set; } = isPrimary;
        public bool IsExhibit { get; } = isExhibit;
        public string SectionId { get; set; }
    }
}
