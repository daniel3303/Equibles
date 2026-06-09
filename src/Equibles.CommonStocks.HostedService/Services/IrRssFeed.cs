using System.Globalization;
using System.Xml.Linq;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Shared plumbing for reading the RSS 2.0 feeds IR platforms publish. Pure (no
/// I/O) so platform parsers built on it stay unit-testable against recorded feed
/// fixtures.
/// </summary>
public static class IrRssFeed
{
    // Column ceilings on IrNewsItem / IrEvent. Defensive truncation so an unusually
    // long source value can't fail the insert.
    public const int MaxTitle = 512;
    public const int MaxUrl = 1024;
    public const int MaxSummary = 4000;

    /// <summary>All RSS items in the document, or empty when the XML is malformed.</summary>
    public static IEnumerable<XElement> Items(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return [];

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        return document.Descendants("item");
    }

    /// <summary>A child element's trimmed text, or null when missing or blank.</summary>
    public static string Value(XElement item, string name)
    {
        var text = item.Element(name)?.Value;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>Parses an RSS date (RFC 2822 with offset) and normalises it to UTC.</summary>
    public static DateTime? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (
            DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed
            )
        )
        {
            return parsed.UtcDateTime;
        }

        return null;
    }

    /// <summary>Trims and caps a value, returning null when missing or blank.</summary>
    public static string Trim(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }
}
