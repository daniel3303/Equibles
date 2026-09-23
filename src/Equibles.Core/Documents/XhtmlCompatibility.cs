using System.Xml;
using System.Xml.Linq;

namespace Equibles.Core.Documents;

/// <summary>Preserves XML empty-element semantics when XHTML is read by an HTML parser.</summary>
public static class XhtmlCompatibility
{
    private const string XhtmlNamespace = "http://www.w3.org/1999/xhtml";
    private static readonly HashSet<string> VoidElements = new(StringComparer.Ordinal)
    {
        "area",
        "base",
        "br",
        "col",
        "embed",
        "hr",
        "img",
        "input",
        "link",
        "meta",
        "param",
        "source",
        "track",
        "wbr",
    };

    public static string ExpandEmptyElements(string source)
    {
        if (string.IsNullOrEmpty(source) || !source.Contains("/>", StringComparison.Ordinal))
            return source;

        try
        {
            using var reader = XmlReader.Create(
                new StringReader(source),
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Ignore,
                    XmlResolver = null,
                    MaxCharactersInDocument = 64L * 1024 * 1024,
                }
            );
            reader.MoveToContent();
            // Ordinary HTML and SEC submission envelopes keep their existing tolerant parser.
            if (reader.LocalName != "html" || reader.NamespaceURI != XhtmlNamespace)
                return source;

            var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            var changed = false;
            foreach (var element in document.Descendants())
            {
                if (
                    !element.IsEmpty
                    || element.Name.NamespaceName != XhtmlNamespace
                    || VoidElements.Contains(element.Name.LocalName)
                )
                    continue;
                // An empty string emits an explicit end tag. <title/> otherwise consumes the
                // rest of the report as title text under HTML rules, hiding all inline facts.
                element.Value = string.Empty;
                changed = true;
            }
            return changed ? document.ToString(SaveOptions.DisableFormatting) : source;
        }
        catch (XmlException)
        {
            // Non-XML filings remain supported by the existing HTML parser; never truncate them.
            return source;
        }
    }
}
