using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

// Some XHTML generators store each printed line in its own positioned div. Keep
// uncertain boundaries intact; only reconstruct a run supported by its geometry
// and unfinished prose. This runs before inline XBRL wrappers are removed.
internal sealed class PositionedProseConversionStep : IHtmlNormalizationStep
{
    private const double CoordinateTolerance = 1;
    private const int MinimumRunLines = 3;
    private const double MinimumLineGapPixels = 4;
    private const double MaximumLineGapPixels = 40;

    public void Execute(IHtmlDocument doc)
    {
        // Inline positions are insufficient when a stylesheet can override them.
        if (doc.QuerySelectorAll("style").Any(style => !string.IsNullOrWhiteSpace(style.TextContent))
            || doc.QuerySelector("link[rel~='stylesheet']") != null)
            return;
        foreach (var page in doc.QuerySelectorAll(".DTRTextContainer"))
        {
            ConvertPage(page, doc);
        }
    }

    private static void ConvertPage(IElement page, IHtmlDocument doc)
    {
        var coordinates = new Dictionary<IElement, (double Left, double Bottom)>();
        foreach (var element in page.QuerySelectorAll("*"))
        {
            var styles = Declarations(element);
            var isLine = element.ClassList.Contains("t");
            if ((!isLine && styles.Count == 0) || string.IsNullOrWhiteSpace(element.TextContent))
                continue;
            // Coordinate overrides, explicit breaks, nested positioning and unknown
            // layout declarations make the entire page's geometry uncertain.
            if (styles.Keys.Any(property => !SupportedStyles.Contains(property)))
                return;
            if (!isLine && !styles.ContainsKey("left") && !styles.ContainsKey("bottom"))
                continue;
            if (!TryCoordinates(element, out var point)
                || (!isLine && element.Children.Length != 0)
                || element.ParentElement?.Closest(".t") != null)
                return;
            if (isLine && element.LocalName != "div")
                return;
            coordinates.Add(element, point);
        }

        var ambiguous = AmbiguousBaselines(coordinates);
        var parents = coordinates.Keys.Select(line => line.ParentElement).Distinct().ToArray();
        foreach (var parent in parents)
        {
            var run = new List<IElement>();
            foreach (var node in parent.ChildNodes.ToArray())
            {
                if (node is IText text && string.IsNullOrWhiteSpace(text.Data))
                    continue;
                if (node is not IElement line || !coordinates.ContainsKey(line)
                    || ambiguous.Contains(line) || !IsProseLine(line))
                {
                    Flush(run, coordinates, doc);
                    continue;
                }
                if (run.Count > 0 && !Continues(run[^1], line, coordinates))
                    Flush(run, coordinates, doc);
                run.Add(line);
            }
            Flush(run, coordinates, doc);
        }
    }

    private static HashSet<IElement> AmbiguousBaselines(
        Dictionary<IElement, (double Left, double Bottom)> coordinates)
    {
        var ordered = coordinates.OrderBy(entry => entry.Value.Bottom).ToArray();
        var ambiguous = new HashSet<IElement>();
        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index].Value.Bottom - ordered[index - 1].Value.Bottom > CoordinateTolerance)
                continue;
            ambiguous.Add(ordered[index - 1].Key);
            ambiguous.Add(ordered[index].Key);
        }
        return ambiguous;
    }

    private static readonly HashSet<string> SupportedStyles = new(StringComparer.OrdinalIgnoreCase)
    {
        "left", "bottom", "display", "letter-spacing", "word-spacing",
    };

    private static bool IsProseLine(IElement line)
    {
        if (line.Children.Length != 0 || !line.ClassList.Contains("t")
            || line.ClassList.Any(name => !IsLineClass(name)))
            return false;
        if (line.Closest("table, li, ul, ol, pre, .item-list-element-wrapper, .DTRTextContainer .DTRTextContainer") != null)
            return false;
        if (!Declarations(line).TryGetValue("display", out var display) || display != "inline")
            return false;

        return true;
    }

    private static bool IsLineClass(string name)
    {
        if (name == "t")
            return true;
        var separator = name.IndexOf('_');
        return name.StartsWith('s') && separator > 1 && separator < name.Length - 1
            && name[1..separator].All(char.IsAsciiLetterOrDigit)
            && name[(separator + 1)..].All(char.IsAsciiDigit);
    }

    private static bool Continues(IElement previous, IElement next,
        Dictionary<IElement, (double Left, double Bottom)> coordinates)
    {
        var before = coordinates[previous];
        var after = coordinates[next];
        var gap = before.Bottom - after.Bottom;
        if (Math.Abs(before.Left - after.Left) > CoordinateTolerance || gap < MinimumLineGapPixels || gap > MaximumLineGapPixels)
            return false;
        var first = previous.TextContent.Trim();
        var second = next.TextContent.Trim();
        if (first.Length < 40 || second.Length < 15 || WordCount(first) < 6 || WordCount(second) < 3
            || !first.Any(char.IsLower))
            return false;
        return char.IsLetterOrDigit(first[^1])
            && (char.IsLower(second[0]) || second[0] is '€' or '$' or '£');
    }

    private static int WordCount(string value) =>
        value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static void Flush(List<IElement> run,
        Dictionary<IElement, (double Left, double Bottom)> coordinates, IHtmlDocument doc)
    {
        if (run.Count >= MinimumRunLines)
        {
            var gaps = run.Zip(run.Skip(1), (before, after) =>
                coordinates[before].Bottom - coordinates[after].Bottom).ToArray();
            if (gaps.Max() - gaps.Min() <= CoordinateTolerance)
                Join(run, doc);
        }
        run.Clear();
    }

    private static void Join(List<IElement> run, IHtmlDocument doc)
    {
        var paragraph = doc.CreateElement("p");
        run[0].ParentElement.InsertBefore(paragraph, run[0]);
        foreach (var line in run)
        {
            if (paragraph.HasChildNodes)
                paragraph.AppendChild(doc.CreateElement("br"));
            foreach (var child in line.ChildNodes.ToArray())
                paragraph.AppendChild(child);
            line.Remove();
        }
    }

    private static bool TryCoordinates(IElement element, out (double Left, double Bottom) point)
    {
        var styles = Declarations(element);
        point = default;
        if (!TryPixel(styles, "left", out var left) || !TryPixel(styles, "bottom", out var bottom))
            return false;
        point = (left, bottom);
        return true;
    }

    private static bool TryPixel(Dictionary<string, string> styles, string property, out double value)
    {
        value = 0;
        return styles.TryGetValue(property, out var raw) && raw.EndsWith("px", StringComparison.Ordinal)
            && double.TryParse(raw.AsSpan(0, raw.Length - 2), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out value) && double.IsFinite(value) && value >= 0;
    }

    private static Dictionary<string, string> Declarations(IElement element)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in (element.GetAttribute("style") ?? "").Split(';'))
        {
            var separator = declaration.IndexOf(':');
            if (separator < 0)
                continue;
            var name = declaration[..separator].Trim();
            if (!result.TryAdd(name, declaration[(separator + 1)..].Trim()))
                result[name] = ""; // A conflicting declaration is not usable geometry.
        }
        return result;
    }
}
