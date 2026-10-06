using AngleSharp;
using AngleSharp.Css;
using AngleSharp.Css.Dom;
using AngleSharp.Css.Parser;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

internal sealed class InlineXbrlProseStyles
{
    private const int MaximumStylesheetCharacters = 2 * 1024 * 1024;
    private const int MaximumStyleRules = 50000;
    private readonly List<string> _unsafeSelectors = [];
    private readonly Dictionary<IElement, bool> _safe = new();
    private readonly CssParser _parser = new(
        new CssParserOptions
        {
            IsIncludingUnknownDeclarations = true,
            IsIncludingUnknownRules = true,
        }
    );
    private bool _valid = true;

    private InlineXbrlProseStyles() => _parser.Error += (_, _) => _valid = false;

    public static InlineXbrlProseStyles TryCreate(IHtmlDocument document)
    {
        if (
            document
                .QuerySelectorAll("link")
                .Any(link =>
                    (link.GetAttribute("rel") ?? "")
                        .Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                        .Contains("stylesheet", StringComparer.OrdinalIgnoreCase)
                )
        )
            return null;
        var result = new InlineXbrlProseStyles();
        var length = 0;
        var count = 0;
        foreach (var sheet in document.QuerySelectorAll("style"))
        {
            var text = sheet.TextContent;
            length += text.Length;
            if (
                length > MaximumStylesheetCharacters
                || text.Contains('&')
                || sheet.HasAttribute("scoped")
                || sheet.HasAttribute("title")
                || (sheet.GetAttribute("media") ?? "").Trim().ToLowerInvariant()
                    is not ("" or "all")
                || (sheet.GetAttribute("type") ?? "").Trim().ToLowerInvariant()
                    is not ("" or "text/css")
            )
                return null;
            var parsed = result.ParseSheet(text);
            if (parsed == null)
                return null;
            foreach (var rule in parsed.Rules)
            {
                if (++count > MaximumStyleRules)
                    return null;
                if (rule is ICssFontFaceRule)
                    continue;
                if (rule is not ICssStyleRule style || style.ToCss().Count(c => c == '{') != 1)
                    return null;
                // Any potentially hiding rule blocks a join even when another rule overrides it.
                if (result.Unsafe(style.Style))
                {
                    if (style.SelectorText.Contains(':'))
                        return null;
                    result._unsafeSelectors.Add(style.SelectorText);
                }
            }
            if (!result._valid)
                return null;
        }
        return result;
    }

    public bool SafeBoundary(IElement paragraph)
    {
        for (var element = paragraph; element != null; element = element.ParentElement)
            if (!Safe(element))
                return false;
        return paragraph.QuerySelectorAll("*").All(Safe);
    }

    private bool Safe(IElement element)
    {
        if (_safe.TryGetValue(element, out var safe))
            return safe;
        var inline = element.GetAttribute("style") ?? "";
        if (inline.Length > 16384)
            return false;
        var declarations = ParseInline(inline);
        safe =
            declarations != null
            && _valid
            && !Unsafe(declarations)
            && !_unsafeSelectors.Any(selector => CouldMatch(element, selector));
        _safe.Add(element, safe);
        return safe;
    }

    private static bool CouldMatch(IElement element, string selector)
    {
        try
        {
            return element.Matches(selector);
        }
        catch (DomException)
        {
            return true;
        }
    }

    private bool Unsafe(ICssStyleDeclaration style) =>
        style.Any(property => !InlineXbrlProseDeclaration.Safe(property, _parser));

    private ICssStyleSheet ParseSheet(string text)
    {
        try
        {
            return _parser.ParseStyleSheet(text);
        }
        catch (NullReferenceException)
        {
            // AngleSharp 0.18 throws for unsupported mask-image declarations.
            return null;
        }
    }

    private ICssStyleDeclaration ParseInline(string text)
    {
        try
        {
            return _parser.ParseDeclaration(text);
        }
        catch (NullReferenceException)
        {
            return null;
        }
    }
}
