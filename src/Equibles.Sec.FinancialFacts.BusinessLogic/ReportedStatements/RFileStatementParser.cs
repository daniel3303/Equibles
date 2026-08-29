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

    private static readonly Regex PresentationContextPattern = new(
        @"defref_[A-Za-z0-9_-]+=[A-Za-z0-9_-]+",
        RegexOptions.Compiled
    );

    private static readonly Regex DurationMonthsPattern = new(
        @"(\d+)\s+Month",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex DurationWeeksPattern = new(
        @"(\d+)\s+Week",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    // One "<subject> in <magnitude>" segment of the scale note; segments are
    // comma-separated, so the subject is everything since the last comma.
    private static readonly Regex ScaleSegmentPattern = new(
        @"(?:^|,)\s*(?<before>.*?)(?:\bin\s+)?(?<scale>Units|Unscaled|Thousands|Millions|Billions)\b(?<after>[^,]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    // The note's leading ISO currency code, rendered as "<code> (<symbol>)" —
    // "USD ($)", "EUR (€)", "CAD ($)".
    private static readonly Regex CurrencyCodePattern = new(
        @"(?<![A-Za-z])([A-Za-z]{3})\s*\(",
        RegexOptions.Compiled
    );

    private static readonly Regex ColumnCurrencyCodePattern = new(
        @"(?<![A-Za-z])([A-Za-z]{3})\s*\(",
        RegexOptions.Compiled
    );

    private static readonly Regex DatePattern = new(
        @"(?<![A-Za-z])(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)(?:\.|[a-z]+)?\s+\d{1,2},\s+\d{4}(?!\d)",
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
        var columns = ParseColumns(table, scaleNote, out var durationByColumn);
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
        var titleCell = table.QuerySelector("th.tl");
        var title = Clean(titleCell?.TextContent);
        if (string.IsNullOrEmpty(title))
        {
            return (null, null, 1);
        }

        // The scale / currency note is the tail after the last " - ", e.g.
        // "... OPERATIONS (Unaudited) - USD ($)  shares in Thousands, $ in Millions".
        var dash = title.LastIndexOf(" - ", StringComparison.Ordinal);
        var breakTail = TextAfterLastBreak(titleCell);
        var note =
            dash >= 0 ? title[(dash + 3)..].Trim()
            : !string.IsNullOrWhiteSpace(breakTail) ? breakTail
            : title;
        var haystack = note;

        return (note, ParseCurrency(haystack), ParseMoneyScale(haystack));
    }

    private static string TextAfterLastBreak(IElement element)
    {
        var lineBreak = element?.QuerySelectorAll("br").LastOrDefault();
        if (lineBreak == null)
        {
            return null;
        }
        var builder = new StringBuilder();
        for (var node = lineBreak.NextSibling; node != null; node = node.NextSibling)
        {
            builder.Append(' ').Append(node.TextContent);
        }
        return Clean(builder.ToString());
    }

    private static string ParseCurrency(string haystack)
    {
        var match = CurrencyCodePattern.Match(haystack);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    private static IReadOnlyList<string> ParseCurrencies(string haystack) =>
        CurrencyCodePattern
            .Matches(haystack ?? string.Empty)
            .Select(match => match.Groups[1].Value.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    // The note scales each unit family in its own segment — "$ in Thousands",
    // "shares in Millions", "$ / shares in Thousands" — and only the money segment may
    // set the statement scale: "USD ($) shares in Thousands" presents dollars UNSCALED,
    // so treating any "in Thousands" as the money scale inflates every money cell 1000×
    // downstream. A subject-less "(In Thousands)" (old-style title) applies to money.
    private static long ParseMoneyScale(string haystack)
    {
        return MoneyScaleSegments(haystack).FirstOrDefault()?.Scale ?? 1L;
    }

    // Columns come from the header rows: the row of period-end dates is the column set; an earlier
    // header row of duration groups ("3 Months Ended", colspan-spanned) maps a duration to each
    // column. A balance sheet has only the date row (no durations) — its columns are instants.
    private static List<ReportedStatementColumn> ParseColumns(
        IElement table,
        string scaleNote,
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
        var titleCurrencies = ParseCurrencies(scaleNote);
        var unambiguousStatementCurrency = titleCurrencies.Count == 1 ? titleCurrencies[0] : null;
        for (var i = 0; i < dateCells.Count; i++)
        {
            var header = Clean(dateCells[i].TextContent);
            var (label, periodEnd) = ParseDateLabel(header);
            var explicitCurrency = ParseColumnCurrency(header);
            var scaleCurrency = explicitCurrency ?? unambiguousStatementCurrency;
            var ambiguousCurrency = explicitCurrency == null && titleCurrencies.Count > 1;
            columns.Add(
                new ReportedStatementColumn
                {
                    Label = label,
                    PeriodEnd = periodEnd,
                    Currency = explicitCurrency ?? unambiguousStatementCurrency,
                    Scale = ambiguousCurrency
                        ? null
                        : ParseColumnMoneyScale(header, scaleCurrency, scaleNote),
                    PerShareScale = ambiguousCurrency
                        ? null
                        : ParseColumnPerShareScale(header, scaleCurrency, scaleNote),
                    Duration = i < durationByColumn.Count ? durationByColumn[i] : null,
                    IsInstant = !hasDurations,
                }
            );
        }
        return columns;
    }

    private static long? ParseColumnMoneyScale(string header, string currency, string scaleNote)
    {
        var headerSegments = MoneyScaleSegments(header);
        if (headerSegments.Count > 0)
        {
            return ResolveColumnMoneyScale(headerSegments, currency);
        }

        var titleSegments = MoneyScaleSegments(scaleNote);
        return titleSegments.Count == 0 ? 1L : ResolveColumnMoneyScale(titleSegments, currency);
    }

    private static long? ResolveColumnMoneyScale(
        IReadOnlyList<MoneyScaleSegment> segments,
        string currency
    )
    {
        if (!string.IsNullOrWhiteSpace(currency))
        {
            var currencyScales = segments
                .Where(segment => segment.Currencies.Contains(currency, StringComparer.Ordinal))
                .Select(segment => segment.Scale)
                .Distinct()
                .ToList();
            if (currencyScales.Count == 1)
            {
                return currencyScales[0];
            }
            if (currencyScales.Count > 1)
            {
                return null;
            }
        }

        var globalScales = segments
            .Where(segment => segment.Currencies.Count == 0)
            .Select(segment => segment.Scale)
            .Distinct()
            .ToList();
        if (globalScales.Count == 1)
        {
            return globalScales[0];
        }
        if (globalScales.Count > 1)
        {
            return null;
        }

        if (
            !string.IsNullOrWhiteSpace(currency)
            && segments.Any(segment => segment.Currencies.Count > 0)
        )
        {
            return null;
        }

        var allScales = segments.Select(segment => segment.Scale).Distinct().ToList();
        return allScales.Count == 1 ? allScales[0] : null;
    }

    private static long? ParseColumnPerShareScale(string header, string currency, string scaleNote)
    {
        var headerSegments = PerShareScaleSegments(header);
        if (headerSegments.Count > 0)
        {
            return ResolveColumnMoneyScale(headerSegments, currency);
        }

        var titleSegments = PerShareScaleSegments(scaleNote);
        return titleSegments.Count == 0 ? 1L : ResolveColumnMoneyScale(titleSegments, currency);
    }

    private static List<MoneyScaleSegment> MoneyScaleSegments(string haystack)
    {
        var result = new List<MoneyScaleSegment>();
        foreach (Match match in ScaleSegmentPattern.Matches(haystack ?? string.Empty))
        {
            var subject = $"{match.Groups["before"].Value} {match.Groups["after"].Value}";
            if (subject.Contains("share", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var scale = match.Groups["scale"].Value.ToLowerInvariant() switch
            {
                "billions" => 1_000_000_000L,
                "millions" => 1_000_000L,
                "thousands" => 1_000L,
                _ => 1L,
            };
            var currencies = CurrencyCodePattern
                .Matches(subject)
                .Select(match => match.Groups[1].Value.ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            result.Add(new MoneyScaleSegment(scale, currencies));
        }
        return result;
    }

    private static List<MoneyScaleSegment> PerShareScaleSegments(string haystack)
    {
        var result = new List<MoneyScaleSegment>();
        foreach (Match match in ScaleSegmentPattern.Matches(haystack ?? string.Empty))
        {
            var subject = $"{match.Groups["before"].Value} {match.Groups["after"].Value}";
            if (
                !subject.Contains("per share", StringComparison.OrdinalIgnoreCase)
                && !Regex.IsMatch(subject, @"/\s*shares?\b", RegexOptions.IgnoreCase)
            )
            {
                continue;
            }
            var scale = match.Groups["scale"].Value.ToLowerInvariant() switch
            {
                "billions" => 1_000_000_000L,
                "millions" => 1_000_000L,
                "thousands" => 1_000L,
                _ => 1L,
            };
            var currencies = CurrencyCodePattern
                .Matches(subject)
                .Select(match => match.Groups[1].Value.ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            result.Add(new MoneyScaleSegment(scale, currencies));
        }
        return result;
    }

    private static List<ReportedStatementRow> ParseRows(IElement table)
    {
        var rows = new List<ReportedStatementRow>();
        var inSection = false;
        string presentationContext = null;

        foreach (var tr in table.QuerySelectorAll("tr"))
        {
            if (tr.ClassList.Contains("rh"))
            {
                var contextCell = tr.QuerySelector("td.pl");
                var onclick = contextCell?.QuerySelector("a")?.GetAttribute("onclick");
                var match = PresentationContextPattern.Match(onclick ?? string.Empty);
                presentationContext = match.Success
                    ? match.Value
                    : $"r-file-heading:{Clean(contextCell?.TextContent)}";
                inSection = false;
                continue;
            }

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
                    PresentationContext = presentationContext,
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
            .Select(c => (Column: c, End: c.PeriodEnd ?? ParseDate(c.Label)))
            .Where(x => x.End != null)
            .ToList();
        if (dated.Count == 0)
        {
            return;
        }

        var maxEnd = dated.Max(x => x.End.Value);
        var primary = dated
            .Where(x => x.End.Value == maxEnd)
            .OrderBy(x => DurationDays(x.Column.Duration))
            .First();

        var end = primary.End.Value;
        var durationDays = DurationDays(primary.Column.Duration);
        result.PrimaryPeriodEnd = end;
        result.PrimaryIsInstant = primary.Column.IsInstant || durationDays == 0;
        result.PrimaryPeriodStart = result.PrimaryIsInstant
            ? end
            : DurationStart(end, primary.Column.Duration);
        result.Currency ??= primary.Column.Currency;
        // Statement.Scale remains compatibility/display metadata. Current consumers bind to
        // Column.Scale; zero records that the primary column's scale was ambiguous.
        result.Scale = primary.Column.Scale ?? 0L;
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
        var (_, date) = ParseDateLabel(text);
        return date;
    }

    private static (string Label, DateOnly? Date) ParseDateLabel(string text)
    {
        var cleaned = Clean(text);
        var match = DatePattern.Match(cleaned ?? string.Empty);
        var candidate = match.Success ? match.Value : cleaned;
        var parseCandidate = Regex.Replace(
            candidate ?? string.Empty,
            @"\bSept(?=\.?\s)",
            "Sep",
            RegexOptions.IgnoreCase
        );
        if (
            DateOnly.TryParseExact(
                parseCandidate,
                DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date
            )
        )
        {
            return (candidate, date);
        }
        return (cleaned, null);
    }

    private static string ParseColumnCurrency(string text)
    {
        var match = ColumnCurrencyCodePattern.Match(text ?? string.Empty);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
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

    private static int DurationWeeks(string duration)
    {
        if (string.IsNullOrEmpty(duration))
        {
            return 0;
        }
        var match = DurationWeeksPattern.Match(duration);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var weeks))
        {
            return 0;
        }
        return weeks is 13 or 14 or 26 or 27 or 39 or 40 or 41 or 52 or 53 ? weeks : 0;
    }

    private static int DurationDays(string duration)
    {
        var weeks = DurationWeeks(duration);
        if (weeks > 0)
        {
            return weeks * 7;
        }
        var months = DurationMonths(duration);
        return months * 31;
    }

    private static DateOnly DurationStart(DateOnly end, string duration)
    {
        var weeks = DurationWeeks(duration);
        return weeks > 0
            ? end.AddDays(1 - weeks * 7)
            : end.AddDays(1).AddMonths(-DurationMonths(duration));
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

    private sealed record MoneyScaleSegment(long Scale, IReadOnlyList<string> Currencies);
}
