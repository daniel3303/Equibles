using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.ReportedStatements;

/// <summary>
/// Parses one of SEC's rendered statement R-files (<c>R#.htm</c>) — the HTML table SEC builds
/// from a filing's own presentation/calculation/label linkbases — into a
/// <see cref="ReportedStatementPayload"/> (period columns + line-item rows) plus the metadata the
/// parse step needs. Values are kept as the issuer presented them (the scale note is preserved,
/// not applied) so the statement renders exactly as filed; each row carries the XBRL concept SEC
/// tagged it with (from the <c>defref_</c> drill-down handle) for free.
/// </summary>
public static class RFileStatementParser
{
    private static readonly string[] DateFormats = ["MMM. d, yyyy", "MMM d, yyyy", "MMMM d, yyyy"];

    // The XBRL concept handle SEC embeds on each row's drill-down link, e.g.
    // defref_us-gaap_NetIncomeLoss → taxonomy "us-gaap", concept "NetIncomeLoss".
    private static readonly Regex ConceptPattern = new(
        @"defref_([A-Za-z0-9-]+)_([A-Za-z0-9]+)",
        RegexOptions.Compiled
    );

    private static readonly Regex DurationMonthsPattern = new(
        @"(\d+)\s+Month",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public static RFileStatement Parse(string html)
    {
        var result = new RFileStatement();
        if (string.IsNullOrWhiteSpace(html))
        {
            return result;
        }

        var document = new HtmlParser().ParseDocument(html);
        var table = document.QuerySelector("table.report");
        if (table == null)
        {
            return result;
        }

        var (scaleNote, currency, scale) = ParseTitle(table);
        var columns = ParseColumns(table, out var durationByColumn);
        var rows = ParseRows(table);
        if (rows.Count == 0)
        {
            return result;
        }

        result.Payload = new ReportedStatementPayload
        {
            ScaleNote = scaleNote,
            Columns = columns,
            Rows = rows,
        };
        result.Currency = currency;
        result.Scale = scale;
        SetPrimaryPeriod(result, columns);
        return result;
    }

    private static (string ScaleNote, string Currency, long Scale) ParseTitle(IElement table)
    {
        var title = Clean(table.QuerySelector("th.tl")?.TextContent);
        if (string.IsNullOrEmpty(title))
        {
            return (null, null, 1);
        }

        // The scale / currency note is the tail after the last " - ", e.g.
        // "... OPERATIONS (Unaudited) - USD ($)  shares in Thousands, $ in Millions".
        var dash = title.LastIndexOf(" - ", StringComparison.Ordinal);
        var note = dash >= 0 ? title[(dash + 3)..].Trim() : null;
        var haystack = note ?? title;

        var currency = haystack.Contains("USD", StringComparison.OrdinalIgnoreCase) ? "USD" : null;
        var scale = haystack switch
        {
            _ when haystack.Contains("in Billions", StringComparison.OrdinalIgnoreCase) =>
                1_000_000_000L,
            _ when haystack.Contains("in Millions", StringComparison.OrdinalIgnoreCase) =>
                1_000_000L,
            _ when haystack.Contains("in Thousands", StringComparison.OrdinalIgnoreCase) => 1_000L,
            _ => 1L,
        };
        return (note, currency, scale);
    }

    // Columns come from the header rows: the row of period-end dates is the column set; an earlier
    // header row of duration groups ("3 Months Ended", colspan-spanned) maps a duration to each
    // column. A balance sheet has only the date row (no durations) — its columns are instants.
    private static List<ReportedStatementColumn> ParseColumns(
        IElement table,
        out List<string> durationByColumn
    )
    {
        durationByColumn = [];
        var headerRows = table
            .QuerySelectorAll("tr")
            .Where(r => r.QuerySelectorAll("th.th").Length > 0)
            .ToList();

        var dateRow = headerRows.LastOrDefault(r =>
            r.QuerySelectorAll("th.th").Any(c => IsDate(c.TextContent))
        );
        if (dateRow == null)
        {
            return [];
        }

        var durationRow = headerRows.FirstOrDefault(r =>
            r != dateRow
            && r.QuerySelectorAll("th.th")
                .Any(c => !IsDate(c.TextContent) && !string.IsNullOrWhiteSpace(c.TextContent))
        );
        var hasDurations = durationRow != null;
        if (hasDurations)
        {
            foreach (var cell in durationRow.QuerySelectorAll("th.th"))
            {
                var span = ParseSpan(cell.GetAttribute("colspan"));
                for (var i = 0; i < span; i++)
                {
                    durationByColumn.Add(Clean(cell.TextContent));
                }
            }
        }

        var columns = new List<ReportedStatementColumn>();
        var dateCells = dateRow.QuerySelectorAll("th.th").ToList();
        for (var i = 0; i < dateCells.Count; i++)
        {
            columns.Add(
                new ReportedStatementColumn
                {
                    Label = Clean(dateCells[i].TextContent),
                    Duration = i < durationByColumn.Count ? durationByColumn[i] : null,
                    IsInstant = !hasDurations,
                }
            );
        }
        return columns;
    }

    private static List<ReportedStatementRow> ParseRows(IElement table)
    {
        var rows = new List<ReportedStatementRow>();
        var inSection = false;

        foreach (var tr in table.QuerySelectorAll("tr"))
        {
            var isTotal = tr.ClassList.Contains("reu") || tr.ClassList.Contains("rou");
            var isData = isTotal || tr.ClassList.Contains("re") || tr.ClassList.Contains("ro");
            if (!isData)
            {
                continue;
            }

            var labelCell = tr.QuerySelector("td.pl");
            if (labelCell == null)
            {
                continue;
            }

            var label = Clean(labelCell.TextContent);
            var (taxonomy, concept) = ParseConcept(
                labelCell.QuerySelector("a")?.GetAttribute("onclick")
            );

            var values = tr.QuerySelectorAll("td.num, td.nump, td.text")
                .Select(c => c.ClassList.Contains("text") ? null : ParseNumber(c.TextContent))
                .ToList();

            var isAbstract = !isTotal && values.All(v => v == null);

            int depth;
            if (isAbstract)
            {
                depth = 0;
                // A real subsection header ends with ":" (e.g. "Operating expenses:") and indents
                // the lines beneath it; a structural "[Abstract]" root does not.
                inSection = label.TrimEnd().EndsWith(':');
            }
            else if (isTotal)
            {
                depth = 0;
                inSection = false;
            }
            else
            {
                depth = inSection ? 1 : 0;
            }

            rows.Add(
                new ReportedStatementRow
                {
                    Label = label,
                    Taxonomy = taxonomy,
                    Concept = concept,
                    Depth = depth,
                    IsAbstract = isAbstract,
                    IsTotal = isTotal,
                    Values = values,
                }
            );
        }
        return rows;
    }

    // The statement reports the current period of its shortest-duration column (a 10-Q's discrete
    // quarter, not the year-to-date), or the newest instant for a balance sheet. Used to resolve
    // the statement's fiscal identity.
    private static void SetPrimaryPeriod(
        RFileStatement result,
        List<ReportedStatementColumn> columns
    )
    {
        var dated = columns
            .Select(c => (Column: c, End: ParseDate(c.Label)))
            .Where(x => x.End != null)
            .ToList();
        if (dated.Count == 0)
        {
            return;
        }

        var maxEnd = dated.Max(x => x.End.Value);
        var primary = dated
            .Where(x => x.End.Value == maxEnd)
            .OrderBy(x => DurationMonths(x.Column.Duration))
            .First();

        var end = primary.End.Value;
        var months = DurationMonths(primary.Column.Duration);
        result.PrimaryPeriodEnd = end;
        result.PrimaryIsInstant = primary.Column.IsInstant || months == 0;
        result.PrimaryPeriodStart = result.PrimaryIsInstant ? end : end.AddMonths(-months);
    }

    private static (string Taxonomy, string Concept) ParseConcept(string onclick)
    {
        if (string.IsNullOrEmpty(onclick))
        {
            return (null, null);
        }
        var match = ConceptPattern.Match(onclick);
        return match.Success ? (match.Groups[1].Value, match.Groups[2].Value) : (null, null);
    }

    private static decimal? ParseNumber(string text)
    {
        var cleaned = Clean(text);
        if (string.IsNullOrEmpty(cleaned))
        {
            return null;
        }

        var negative = cleaned.StartsWith('(') && cleaned.EndsWith(')');
        cleaned = cleaned
            .Replace("(", string.Empty)
            .Replace(")", string.Empty)
            .Replace("$", string.Empty)
            .Replace(",", string.Empty)
            .Replace("%", string.Empty)
            .Trim();
        if (
            !decimal.TryParse(
                cleaned,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var value
            )
        )
        {
            return null;
        }
        return negative ? -value : value;
    }

    private static DateOnly? ParseDate(string text)
    {
        var cleaned = Clean(text);
        if (
            DateOnly.TryParseExact(
                cleaned,
                DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date
            )
        )
        {
            return date;
        }
        return null;
    }

    private static bool IsDate(string text) => ParseDate(text) != null;

    private static int DurationMonths(string duration)
    {
        if (string.IsNullOrEmpty(duration))
        {
            return 0;
        }
        var match = DurationMonthsPattern.Match(duration);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var months))
        {
            return months;
        }
        return duration.Contains("Year", StringComparison.OrdinalIgnoreCase) ? 12 : 0;
    }

    private static int ParseSpan(string colspan) =>
        int.TryParse(colspan, out var span) && span > 0 ? span : 1;

    // Normalizes SEC's rendered cell text: collapse every whitespace flavor (NBSP from &#160; in
    // particular) to a plain space, drop zero-width marks, and trim. Avoids literal special-char
    // constants so the source stays clean.
    private static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                // NBSP (&#160;) and friends collapse to a plain space.
                builder.Append(' ');
                continue;
            }
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.Format)
            {
                // Drop zero-width space, BOM, soft hyphen, etc. so they never pollute a label/value.
                continue;
            }
            builder.Append(ch);
        }
        return builder.ToString().Trim();
    }
}
