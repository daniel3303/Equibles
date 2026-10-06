using System.Xml;
using AngleSharp;
using AngleSharp.Css;
using AngleSharp.Css.Dom;
using AngleSharp.Css.Parser;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace Equibles.Sec.BusinessLogic.Normalizers;

// Geometry is read in an isolated, loader-free context. The original DOM remains
// the source of every character; this context never loads fonts, images or scripts.
internal sealed class XhtmlStylesheetGeometry : IDisposable
{
    private const int MaximumMarkupCharacters = 8 * 1024 * 1024;
    private const int MaximumStylesheetCharacters = 1024 * 1024;
    private const string Xhtml = "http://www.w3.org/1999/xhtml";
    private readonly IBrowsingContext _context;
    private readonly IStyleCollection _styles;
    private readonly Dictionary<IElement, ICssStyleDeclaration> _computedStyles = new();

    private XhtmlStylesheetGeometry(
        IBrowsingContext context,
        IHtmlDocument document,
        IRenderDevice device
    )
    {
        _context = context;
        Document = document;
        _styles = document.DefaultView.GetStyleCollection(device);
    }

    public IHtmlDocument Document { get; }

    public ICssStyleDeclaration Style(IElement element)
    {
        if (!_computedStyles.TryGetValue(element, out var style))
            _computedStyles.Add(element, style = _styles.ComputeExplicitStyle(element));
        return style;
    }

    public static XhtmlStylesheetGeometry TryCreate(string markup, IHtmlDocument original)
    {
        if (
            markup.Length > MaximumMarkupCharacters
            || original.QuerySelector(".pf > .pc > .t") == null
        )
            return null;
        if (
            original
                .QuerySelectorAll("link")
                .Any(element =>
                    (element.GetAttribute("rel") ?? "")
                        .Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                        .Contains("stylesheet", StringComparer.OrdinalIgnoreCase)
                )
        )
            return null;
        var sheets = ReadStylesheets(markup);
        if (sheets == null || sheets.Count == 0 || !SupportedRules(sheets))
            return null;

        var device = new DefaultRenderDevice
        {
            DeviceWidth = 1600,
            DeviceHeight = 1200,
            FontSize = 16,
        };
        var context = BrowsingContext.New(
            Configuration
                .Default.WithCss(
                    new CssParserOptions
                    {
                        IsIncludingUnknownDeclarations = true,
                        IsIncludingUnknownRules = true,
                    }
                )
                .WithRenderDevice(device)
        );
        try
        {
            var document = new HtmlParser(
                new HtmlParserOptions { IsAcceptingCustomElementsEverywhere = true },
                context
            ).ParseDocument(markup);
            var styles = document.QuerySelectorAll("style");
            if (styles.Length != sheets.Count)
            {
                context.Dispose();
                return null;
            }
            // XHTML decodes entities inside styles; HTML's raw-text parser does not.
            // Set DOM text after parsing so decoded CSS cannot introduce HTML nodes.
            for (var index = 0; index < styles.Length; index++)
                styles[index].TextContent = sheets[index];
            return new(context, document, device);
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static List<string> ReadStylesheets(string markup)
    {
        try
        {
            using var input = new StringReader(markup);
            using var reader = XmlReader.Create(
                input,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = MaximumMarkupCharacters,
                }
            );
            var sheets = new List<string>();
            var length = 0;
            while (reader.Read())
            {
                if (reader.Depth > 128 || reader.NodeType == XmlNodeType.ProcessingInstruction)
                    return null;
                if (reader.NodeType != XmlNodeType.Element)
                    continue;
                if (
                    reader.Depth == 0
                    && (reader.LocalName != "html" || reader.NamespaceURI != Xhtml)
                )
                    return null;
                if (reader.LocalName != "style")
                    continue;
                if (reader.NamespaceURI != Xhtml || sheets.Count >= 32)
                    return null;
                if (
                    (reader.GetAttribute("media") ?? "").Trim().ToLowerInvariant()
                        is not ("" or "all" or "screen" or "print")
                    || (reader.GetAttribute("type") ?? "").Trim().ToLowerInvariant()
                        is not ("" or "text/css")
                    || reader.GetAttribute("title") != null
                    || reader.GetAttribute("scoped") != null
                )
                    return null;
                using var subtree = reader.ReadSubtree();
                subtree.Read();
                var text = subtree.ReadElementContentAsString();
                length += text.Length;
                if (length > MaximumStylesheetCharacters)
                    return null;
                sheets.Add(text);
            }
            return sheets;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static bool SupportedRules(List<string> sheets)
    {
        var valid = true;
        var parser = new CssParser(
            new CssParserOptions
            {
                IsIncludingUnknownDeclarations = true,
                IsIncludingUnknownRules = true,
            }
        );
        parser.Error += (_, _) => valid = false;
        var count = 0;
        bool Supported(IEnumerable<ICssRule> rules)
        {
            foreach (var rule in rules)
            {
                if (++count > 20000)
                    return false;
                switch (rule)
                {
                    case ICssStyleRule style:
                        // Generated text and nested rules make a printed line incomplete.
                        var content = style.Style.GetPropertyValue("content");
                        if (
                            content is not ("" or "none" or "normal" or "\"\"" or "''")
                            || style.ToCss().Count(character => character == '{') != 1
                        )
                            return false;
                        break;
                    case ICssFontFaceRule:
                        break;
                    case ICssMediaRule media
                        when media.Media.MediaText.Equals(
                            "print",
                            StringComparison.OrdinalIgnoreCase
                        ):
                        break;
                    case ICssMediaRule media
                        when media.Media.MediaText.ToLowerInvariant() is "screen" or "all":
                        if (!Supported(media.Rules))
                            return false;
                        break;
                    case ICssMediaRule media
                        when media.Rules.All(rule =>
                            rule is ICssStyleRule style
                            && style.Style.All(property =>
                                property.Name == "text-shadow" && property.Value == "none"
                                || property.Name == "-webkit-text-stroke"
                                    && property.Value.EndsWith(
                                        "transparent",
                                        StringComparison.Ordinal
                                    )
                            )
                        ):
                        // Some exporters switch between transparent text paint methods by
                        // device ratio. Neither alternative can move a line or add text.
                        break;
                    default:
                        return false;
                }
            }
            return true;
        }
        foreach (var sheet in sheets)
            if (!Supported(parser.ParseStyleSheet(sheet).Rules) || !valid)
                return false;
        return true;
    }

    public void Dispose() => _context.Dispose();
}
