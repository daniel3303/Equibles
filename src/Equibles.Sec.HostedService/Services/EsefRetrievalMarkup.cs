using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Equibles.Sec.HostedService.Services;

// The original keeps its presentation. Only the retrieval copy sheds unused layout metadata.
internal static partial class EsefRetrievalMarkup
{
    private const string XhtmlNamespace = "http://www.w3.org/1999/xhtml";
    private static readonly HashSet<string> LayoutProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "font-family",
        "font-size",
        "letter-spacing",
        "word-spacing",
        "-webkit-text-stroke",
        "width",
        "margin-left",
        "margin",
        "margin-top",
        "margin-bottom",
        "margin-right",
        "padding",
        "padding-top",
        "padding-bottom",
        "padding-left",
        "padding-right",
        "border",
        "border-top",
        "border-bottom",
        "border-left",
        "border-right",
        "border-collapse",
        "border-top-width",
        "border-bottom-width",
        "border-top-style",
        "border-bottom-style",
        "vertical-align",
        "height",
        "left",
        "top",
        "position",
        "white-space",
        "line-height",
    };

    internal static string Compact(string source, int maximumCharacters)
    {
        if (string.IsNullOrEmpty(source) || source.Length <= maximumCharacters)
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
            if (reader.LocalName != "html" || reader.NamespaceURI != XhtmlNamespace)
                return source;
            var output = new StringBuilder();
            using var writer = XmlWriter.Create(
                output,
                new XmlWriterSettings
                {
                    OmitXmlDeclaration = true,
                    Indent = false,
                    NewLineHandling = NewLineHandling.Entitize,
                }
            );
            var scriptContainer = false;
            do
            {
                if (reader.Depth == 1)
                    scriptContainer =
                        reader.NodeType == XmlNodeType.Element
                        && reader.NamespaceURI == XhtmlNamespace
                        && reader.LocalName is "head" or "body";
                if (
                    reader.NodeType == XmlNodeType.Element
                    && reader.LocalName == "script"
                    && reader.NamespaceURI == XhtmlNamespace
                )
                {
                    // Only root head/body children are presentation scripts; other
                    // locations may contribute to a fact, context or continuation.
                    if (reader.Depth != 2 || !scriptContainer || !SkipScript(reader))
                        return source;
                }
                else
                    CopyNode(reader, writer);
                writer.Flush();
                // Output only grows; never pass a prefix to the document normalizer.
                if (output.Length > maximumCharacters)
                    return source;
            } while (reader.Read());
            writer.Flush();
            return output.ToString();
        }
        catch (XmlException)
        {
            // Ambiguous or non-XML input keeps the existing size refusal.
            return source;
        }
    }

    private static bool SkipScript(XmlReader reader)
    {
        // Viewer data is already omitted by Markdown conversion. Refuse nested XML,
        // which could contain financial facts, instead of hiding it from the parser.
        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
            if (subtree.Depth > 0 && subtree.NodeType == XmlNodeType.Element)
                return false;
        return true;
    }

    private static void CopyNode(XmlReader reader, XmlWriter writer)
    {
        switch (reader.NodeType)
        {
            case XmlNodeType.Element:
                CopyElement(reader, writer);
                break;
            case XmlNodeType.EndElement:
                writer.WriteFullEndElement();
                break;
            case XmlNodeType.Text:
                writer.WriteString(reader.Value);
                break;
            case XmlNodeType.Whitespace:
            case XmlNodeType.SignificantWhitespace:
                writer.WriteWhitespace(reader.Value);
                break;
            case XmlNodeType.CDATA:
                writer.WriteCData(reader.Value);
                break;
            case XmlNodeType.Comment:
                writer.WriteComment(reader.Value);
                break;
            case XmlNodeType.ProcessingInstruction:
                writer.WriteProcessingInstruction(reader.Name, reader.Value);
                break;
        }
    }

    private static void CopyElement(XmlReader reader, XmlWriter writer)
    {
        var isXhtml = reader.NamespaceURI == XhtmlNamespace;
        var elementName = reader.LocalName;
        var compactClass = isXhtml && CanCompactClass(elementName, reader.GetAttribute("class"));
        var empty = reader.IsEmptyElement;
        writer.WriteStartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);
        if (reader.MoveToFirstAttribute())
        {
            do
            {
                var value =
                    isXhtml && reader.NamespaceURI.Length == 0 && reader.LocalName == "style"
                        ? CompactStyle(reader.Value)
                        : reader.Value;
                if (compactClass && reader.NamespaceURI.Length == 0)
                {
                    // Nonempty class/id metadata changes Markdown paragraph boundaries.
                    // Retain that branch with one neutral class; a span's id is then redundant.
                    if (reader.LocalName == "class")
                        value = "x";
                    else if (elementName == "span" && reader.LocalName == "id")
                        value = "";
                }
                writer.WriteAttributeString(
                    reader.Prefix,
                    reader.LocalName,
                    reader.NamespaceURI,
                    value
                );
            } while (reader.MoveToNextAttribute());
            reader.MoveToElement();
        }
        if (empty)
            writer.WriteEndElement();
    }

    private static readonly string[] SemanticClasses =
    [
        "item-list-element-wrapper",
        "math",
        "footnote",
        "line-block",
        "highlight",
        "language-",
        "lang-",
        "brush:",
    ];

    private static bool CanCompactClass(string element, string value)
    {
        if (element is not ("span" or "div") || string.IsNullOrWhiteSpace(value))
            return false;
        // These conventions are consumed by the normalizer or ReverseMarkdown readers,
        // including code-language classes read from a preformatted block's parent.
        foreach (var fragment in SemanticClasses)
        {
            if (value.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    [GeneratedRegex(
        @"\b(?:rgb|rgba)\([0-9.,% +\-]+\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex NumericColorFunction();

    private static string CompactStyle(string style)
    {
        // Flat numeric color functions contain no declaration separators.
        // All other functions, strings, escapes and comments keep the whole attribute.
        var syntax = NumericColorFunction().Replace(style, "");
        if (syntax.IndexOfAny(['\'', '"', '\\', '(', ')', '{', '}', '@', '/']) >= 0)
            return style;
        return string.Join(
            ';',
            style
                .Split(';')
                .Where(declaration =>
                {
                    var colon = declaration.IndexOf(':');
                    return colon < 0 || !LayoutProperties.Contains(declaration[..colon].Trim());
                })
        );
    }
}
