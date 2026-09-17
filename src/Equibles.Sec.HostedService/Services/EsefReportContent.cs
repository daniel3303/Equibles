using System.Text;
using Equibles.Sec.BusinessLogic;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Turns an ESEF report into the document's retrieval body. The report as served stays on the document as
/// its XBRL envelope; this is the text that chunks and embeds, so it carries no markup and no encoded bytes.
/// </summary>
public static class EsefReportContent
{
    private const string Marker = "data:";
    private const string Encoded = ";base64,";

    /// <summary>
    /// How much of a report is turned into retrieval text. Parsing costs about twenty times the input in
    /// live memory and sixty-seven times it in total allocation, both measured, so a report whose readable
    /// half runs past this is stored with an empty body rather than parsed. It still keeps its envelope and
    /// still yields facts; only the text a reader would search is left out.
    /// </summary>
    public const int MaxRetrievalHtmlChars = 16 * 1024 * 1024;

    /// <summary>
    /// Drops every encoded payload the report embeds in itself. A European report carries its figures and
    /// its typefaces inline rather than beside it: in one sampled report a font in a style rule was 7.5 MB
    /// of 10.4 MB and the images a further 1.3 MB. None of it is readable text, so it goes before the
    /// document is parsed rather than after.
    /// </summary>
    public static string StripEmbeddedData(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;
        var builder = new StringBuilder(html.Length);
        var position = 0;
        while (true)
        {
            var marker = html.IndexOf(Marker, position, StringComparison.OrdinalIgnoreCase);
            if (marker <= 0)
                break;
            // An attribute closes on its own quote and a style rule's url() on its bracket. Anything else
            // is prose, and prose is kept.
            var closer = html[marker - 1] switch
            {
                '"' => '"',
                '\'' => '\'',
                '(' => ')',
                _ => '\0',
            };
            var end = closer == '\0' ? -1 : html.IndexOf(closer, marker);
            if (end < 0)
            {
                builder.Append(html, position, marker - position + Marker.Length);
                position = marker + Marker.Length;
                continue;
            }
            // Only an encoded payload is dropped. A quoted phrase is not an image just because it opens
            // with the word.
            if (html.IndexOf(Encoded, marker, end - marker, StringComparison.OrdinalIgnoreCase) < 0)
            {
                builder.Append(html, position, end - position);
                position = end;
                continue;
            }
            builder.Append(html, position, marker - position);
            position = end;
        }
        builder.Append(html, position, html.Length - position);
        return builder.ToString();
    }

    /// <summary>
    /// The report's text, as UTF-8 Markdown. The same two steps the SEC lane applies to a filing, so one
    /// retrieval corpus is written one way; only the envelope selection differs, because an ESEF report
    /// arrives on its own rather than inside a submission.
    /// </summary>
    public static byte[] Build(
        string html,
        ISecDocumentHtmlNormalizer normalizer,
        ISecDocumentHtmlToMarkdownConverter converter
    )
    {
        ArgumentNullException.ThrowIfNull(normalizer);
        ArgumentNullException.ThrowIfNull(converter);
        var stripped = StripEmbeddedData(html);
        if (string.IsNullOrEmpty(stripped) || stripped.Length > MaxRetrievalHtmlChars)
            return [];
        var normalized = normalizer.NormalizeFragment(stripped);
        return Encoding.UTF8.GetBytes(converter.Convert(normalized) ?? string.Empty);
    }
}
