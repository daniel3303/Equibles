using System.Net;
using System.Text.RegularExpressions;
using Equibles.Core.Identity;
using Equibles.Integrations.Gpw.Models;
using HtmlAgilityPack;

namespace Equibles.Integrations.Gpw;

public static partial class GpwParser
{
    [GeneratedRegex(@"^company-factsheet\?isin=(?<isin>[A-Z0-9]{12})$")]
    private static partial Regex FactsheetLink();

    [GeneratedRegex(@"^(?<name>.+?)\s*\((?<isin>[A-Z0-9]{12})\)$")]
    private static partial Regex Heading();

    // Cells are addressed by their column class, never by position: the bid and ask groups repeat col15/col16.
    public static List<GpwQuotation> ReadTable(string html)
    {
        var document = Html(html);
        var rows = document.DocumentNode.SelectNodes(
            "//table//tr[contains(concat(' ', normalize-space(@class), ' '), ' trclass ')]"
        );
        if (rows == null || rows.Count == 0)
            throw new InvalidDataException("GPW quotations table contains no rows.");
        var quotations = new List<GpwQuotation>();
        foreach (var row in rows)
        {
            var link = Cell(row, "col2")?.SelectSingleNode(".//a[@href]");
            var href =
                link == null ? null : FactsheetLink().Match(link.GetAttributeValue("href", ""));
            var quotation = new GpwQuotation
            {
                Name = Clean(link?.InnerText),
                Isin = Clean(Cell(row, "col3")?.InnerText),
                Shortcut = Clean(Cell(row, "col4")?.InnerText),
                Currency = Clean(Cell(row, "col5")?.InnerText),
                MarketIdentifierCode = Clean(Cell(row, "col21")?.InnerText),
            };
            if (
                href is not { Success: true }
                || href.Groups["isin"].Value != quotation.Isin
                || !InternationalSecurityIdentifiers.IsValidIsin(quotation.Isin)
                || string.IsNullOrWhiteSpace(quotation.Name)
                || quotation.Name.Length > 500
                || string.IsNullOrWhiteSpace(quotation.Shortcut)
                || quotation.Shortcut.Length > 32
                || !IsCurrencyCode(quotation.Currency)
                || !IsMarketIdentifierCode(quotation.MarketIdentifierCode)
            )
                throw new InvalidDataException(
                    "GPW quotations row lacks valid stated listing identity."
                );
            quotations.Add(quotation);
        }
        return quotations;
    }

    public static GpwCompanyFactsheet ReadFactsheet(string html)
    {
        var document = Html(html);
        var heading = Clean(document.DocumentNode.SelectSingleNode("//h1[@id='setH1']")?.InnerText);
        var shortcut = Clean(
            document
                .DocumentNode.SelectSingleNode("//input[@id='glsSkrot']")
                ?.GetAttributeValue("value", null)
        );
        var match = heading == null ? null : Heading().Match(heading);
        if (match is not { Success: true } || string.IsNullOrWhiteSpace(shortcut))
            throw new InvalidDataException("GPW company page has changed shape.");
        return new GpwCompanyFactsheet
        {
            Name = match.Groups["name"].Value,
            Isin = match.Groups["isin"].Value,
            Shortcut = shortcut,
        };
    }

    private static HtmlNode Cell(HtmlNode row, string column) =>
        row.SelectSingleNode(
            $"./td[contains(concat(' ', normalize-space(@class), ' '), ' {column} ')]"
        );

    private static string Clean(string value)
    {
        if (value == null)
            return null;
        var text = WebUtility.HtmlDecode(value).Replace(' ', ' ').Trim();
        return text.Length == 0 ? null : text;
    }

    private static bool IsCurrencyCode(string value) =>
        value is { Length: 3 } && value.All(character => character is >= 'A' and <= 'Z');

    private static bool IsMarketIdentifierCode(string value) =>
        value is { Length: 4 }
        && value.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9');

    private static HtmlDocument Html(string value)
    {
        var document = new HtmlDocument();
        document.LoadHtml(value ?? "");
        return document;
    }
}
