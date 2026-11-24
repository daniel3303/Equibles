using System.Text;
using System.Xml;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// The text of a report too large for the document conversion, written while its markup is read
/// from start to end. Nothing of the report is held but the line or table row being read, so
/// memory does not grow with the report. The text is plainer than the conversion's: lines,
/// headings and table rows, with no links, emphasis or layout read from stylesheets.
/// </summary>
internal static class EsefStreamedText
{
    private const string Xhtml = "http://www.w3.org/1999/xhtml";
    private const string InlineXbrl = "http://www.xbrl.org/2013/inlineXBRL";
    private const string Svg = "http://www.w3.org/2000/svg";

    // Never shown to a reader: the document head and code.
    private static readonly HashSet<string> Unread = new(StringComparer.Ordinal)
    {
        "head",
        "script",
        "style",
        "noscript",
        "template",
    };

    // Elements that start and end a line of their own.
    private static readonly HashSet<string> Lines = new(StringComparer.Ordinal)
    {
        "address",
        "article",
        "aside",
        "body",
        "caption",
        "dd",
        "div",
        "dl",
        "dt",
        "figcaption",
        "figure",
        "footer",
        "form",
        "header",
        "hr",
        "main",
        "nav",
        "ol",
        "pre",
        "section",
        "svg",
        "ul",
    };

    // Lines followed by an empty one, as a paragraph is.
    private static readonly HashSet<string> Paragraphs = new(StringComparer.Ordinal)
    {
        "blockquote",
        "h1",
        "h2",
        "h3",
        "h4",
        "h5",
        "h6",
        "li",
        "p",
    };

    /// <summary>The report's text, or null when it is not XHTML an XML reader accepts.</summary>
    public static string Read(string xhtml, int maximumCharacters) =>
        string.IsNullOrEmpty(xhtml)
            ? null
            : Read(XmlReader.Create(new StringReader(xhtml), Settings()), maximumCharacters);

    /// <summary>The report's text, or null when it is not XHTML an XML reader accepts.</summary>
    public static string Read(byte[] xhtml, int maximumCharacters) =>
        xhtml is not { Length: > 0 }
            ? null
            : Read(
                XmlReader.Create(new MemoryStream(xhtml, writable: false), Settings()),
                maximumCharacters
            );

    private static XmlReaderSettings Settings() =>
        new()
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersInDocument = EsefReportEnvelope.MaximumSourceBytes,
        };

    private static string Read(XmlReader reader, int maximumCharacters)
    {
        using (reader)
        {
            try
            {
                reader.MoveToContent();
                if (reader.LocalName != "html" || reader.NamespaceURI != Xhtml)
                    return null;
                var text = new Text(maximumCharacters);
                // Skip leaves the reader on the node after the element, so only a node that was
                // read through asks for the next one.
                var more = reader.Read();
                while (more)
                {
                    if (reader.NodeType == XmlNodeType.Element && IsUnread(reader))
                    {
                        reader.Skip();
                        more = !reader.EOF;
                        continue;
                    }
                    text.Take(reader);
                    if (text.Full)
                        return null;
                    more = reader.Read();
                }
                return text.End();
            }
            catch (XmlException)
            {
                return null;
            }
        }
    }

    private static bool IsUnread(XmlReader reader)
    {
        if (reader.NamespaceURI == InlineXbrl)
            // The header holds contexts, units and facts hidden from the page.
            return reader.LocalName == "header";
        // A drawing's words stay, as the conversion keeps them; its code and hidden layers do not.
        if (reader.NamespaceURI == Svg && reader.LocalName is "style" or "script")
            return true;
        if (reader.NamespaceURI != Xhtml && reader.NamespaceURI != Svg)
            return false;
        if (reader.NamespaceURI == Xhtml && Unread.Contains(reader.LocalName))
            return true;
        var style = reader.GetAttribute("style");
        return style != null
            && style.Contains("none", StringComparison.OrdinalIgnoreCase)
            && style
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Contains("display:none", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Text(int maximumCharacters)
    {
        private readonly StringBuilder _written = new();
        private readonly StringBuilder _line = new();
        private readonly StringBuilder _cell = new();
        private readonly List<string> _row = [];
        private int _tables;
        private int _rows;
        private int _heading;
        private bool _afterBlank = true;

        public bool Full => _written.Length > maximumCharacters;

        public void Take(XmlReader reader)
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    (_tables > 0 ? _cell : _line).Append(reader.Value);
                    break;
                case XmlNodeType.Element when Shapes(reader):
                    Open(reader.LocalName);
                    // An element with no content has no end of its own to read.
                    if (reader.IsEmptyElement)
                        Close(reader.LocalName);
                    break;
                case XmlNodeType.EndElement when Shapes(reader):
                    Close(reader.LocalName);
                    break;
            }
        }

        // XHTML elements and a drawing's own element, which stands on lines of its own.
        private static bool Shapes(XmlReader reader) =>
            reader.NamespaceURI == Xhtml
            || (reader.NamespaceURI == Svg && reader.LocalName == "svg");

        public string End()
        {
            EndLine();
            return _written.ToString().TrimEnd();
        }

        private void Open(string name)
        {
            if (name == "table")
            {
                if (_tables++ == 0)
                {
                    EndLine();
                    EmptyLine();
                    _rows = 0;
                    _cell.Clear();
                }
                return;
            }
            // A table inside a cell is read as that cell's words.
            if (_tables > 0)
            {
                if (_tables == 1 && name == "tr")
                    _row.Clear();
                else if (_tables == 1 && name is "caption" or "td" or "th")
                    _cell.Clear();
                else if (name is "br" or "td" or "th" or "tr" || Breaks(name))
                    _cell.Append(' ');
                return;
            }
            if (name == "br")
                EndLine();
            else if (Breaks(name))
            {
                EndLine();
                if (name.Length == 2 && name[0] == 'h' && char.IsAsciiDigit(name[1]))
                    _heading = name[1] - '0';
            }
        }

        private void Close(string name)
        {
            if (name == "table")
            {
                if (_tables > 0 && --_tables == 0)
                    EmptyLine();
                return;
            }
            if (_tables > 0)
            {
                if (_tables == 1 && name == "caption")
                    EndCaption();
                else if (_tables == 1 && name is "td" or "th")
                    _row.Add(Collapse(_cell).Replace("|", "\\|", StringComparison.Ordinal));
                else if (_tables == 1 && name == "tr")
                    EndRow();
                return;
            }
            if (!Breaks(name))
                return;
            EndLine();
            if (name.Length == 2 && name[0] == 'h')
                _heading = 0;
            if (Paragraphs.Contains(name))
                EmptyLine();
        }

        private static bool Breaks(string name) =>
            Lines.Contains(name) || Paragraphs.Contains(name);

        private void EndLine()
        {
            var line = Collapse(_line);
            _line.Clear();
            if (line.Length == 0)
                return;
            if (_heading is >= 1 and <= 6)
            {
                EmptyLine();
                _written.Append('#', _heading).Append(' ');
            }
            _written.Append(line).Append('\n');
            _afterBlank = false;
        }

        // A table's caption is a paragraph before its rows, as the conversion writes it.
        private void EndCaption()
        {
            var caption = Collapse(_cell);
            _cell.Clear();
            if (caption.Length == 0)
                return;
            _written.Append(caption).Append('\n');
            _afterBlank = false;
            EmptyLine();
        }

        private void EndRow()
        {
            if (_row.TrueForAll(cell => cell.Length == 0))
                return;
            _written.Append("| ").AppendJoin(" | ", _row).Append(" |\n");
            // Markdown reads a table by the rule under its first row.
            if (_rows++ == 0)
                _written.Append('|').Insert(_written.Length, " --- |", _row.Count).Append('\n');
            _afterBlank = false;
        }

        private void EmptyLine()
        {
            if (_afterBlank)
                return;
            _written.Append('\n');
            _afterBlank = true;
        }

        // One space for any run of white space, the no-break space among it, and none at the ends.
        private static string Collapse(StringBuilder text)
        {
            var collapsed = new StringBuilder(text.Length);
            var space = false;
            foreach (var chunk in text.GetChunks())
            {
                foreach (var character in chunk.Span)
                {
                    if (char.IsWhiteSpace(character))
                    {
                        space = collapsed.Length > 0;
                        continue;
                    }
                    if (space)
                        collapsed.Append(' ');
                    space = false;
                    collapsed.Append(character);
                }
            }
            return collapsed.ToString();
        }
    }
}
