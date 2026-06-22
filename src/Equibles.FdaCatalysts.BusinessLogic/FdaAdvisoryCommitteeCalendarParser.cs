using System.Globalization;
using Equibles.FdaCatalysts.Data.Models;
using HtmlAgilityPack;

namespace Equibles.FdaCatalysts.BusinessLogic;

/// <summary>
/// Parses the FDA.gov advisory-committee calendar's table into <see cref="FdaCatalyst"/> rows.
/// The calendar is a client-rendered DataTable, so the caller must supply the rendered DOM
/// (fetched through the headless browser path), not a plain HTTP response — a plain GET returns
/// the page chrome with an empty table body.
///
/// Every field comes from an explicit calendar column (Start Date, End Date, Meeting, Center) or
/// the per-meeting anchor's slug, so nothing is recovered from prose. Rows without a parseable
/// Start Date or without the meeting-page anchor are skipped: they carry no authoritative date to
/// place on a timeline and no stable natural key.
/// </summary>
public static class FdaAdvisoryCommitteeCalendarParser
{
    private const string CalendarHost = "https://www.fda.gov";

    public static IReadOnlyList<FdaCatalyst> Parse(string html)
    {
        var catalysts = new List<FdaCatalyst>();
        if (string.IsNullOrWhiteSpace(html))
        {
            return catalysts;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var table = doc.DocumentNode.SelectSingleNode(
            "//table[contains(@class, 'lcds-datatable--advisory-committee-calendar')]"
        );
        if (table == null)
        {
            return catalysts;
        }

        // Resolve column positions from the header so a reordered table still maps correctly.
        var headerCells = table.SelectNodes(".//thead//th") ?? table.SelectNodes(".//thead//td");
        if (headerCells == null)
        {
            return catalysts;
        }

        var columns = headerCells
            .Select(h => HtmlEntity.DeEntitize(h.InnerText).Trim().ToLowerInvariant())
            .ToList();
        var startCol = columns.FindIndex(c => c.Contains("start date"));
        var endCol = columns.FindIndex(c => c.Contains("end date"));
        var meetingCol = columns.FindIndex(c => c.Contains("meeting"));
        var centerCol = columns.FindIndex(c => c.Contains("center"));
        if (startCol < 0 || meetingCol < 0 || centerCol < 0)
        {
            return catalysts;
        }

        var rows = table.SelectNodes(".//tbody//tr");
        if (rows == null)
        {
            return catalysts;
        }

        var minCells = Math.Max(startCol, Math.Max(meetingCol, centerCol));
        var seenSlugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var cells = row.SelectNodes("./td");
            if (cells == null || cells.Count <= minCells)
            {
                continue;
            }

            // The Meeting cell's anchor carries the authoritative per-meeting slug and title.
            var link = cells[meetingCol].SelectSingleNode(".//a[@href]");
            if (link == null)
            {
                continue;
            }

            var href = link.GetAttributeValue("href", string.Empty).Trim();
            var slug = ExtractSlug(href);
            if (slug.Length == 0)
            {
                continue;
            }

            var meetingDate = ParseColumnDate(cells[startCol].InnerText);
            if (meetingDate == null)
            {
                continue;
            }

            var title = HtmlEntity.DeEntitize(link.InnerText).Trim();
            if (title.Length == 0)
            {
                continue;
            }

            var center = HtmlEntity.DeEntitize(cells[centerCol].InnerText).Trim();
            if (center.Length == 0)
            {
                continue;
            }

            // Dedup only after the row is known good. Registering the slug earlier let a skipped row
            // (e.g. one with no parseable Start Date) consume the slug and suppress a later, fully
            // valid listing of the same meeting — dropping it entirely (#3861).
            if (!seenSlugs.Add(slug))
            {
                continue;
            }

            var endDate =
                endCol >= 0 && endCol < cells.Count
                    ? ParseColumnDate(cells[endCol].InnerText)
                    : null;

            catalysts.Add(
                new FdaCatalyst
                {
                    CatalystType = FdaCatalystType.AdvisoryCommittee,
                    MeetingDate = meetingDate.Value,
                    EndDate = endDate,
                    Center = center,
                    Title = title,
                    SourceReference = slug,
                    SourceUrl = ToAbsoluteUrl(href),
                }
            );
        }

        return catalysts;
    }

    // Calendar dates render as "MM/dd/yyyy hh:mm tt zzz" (e.g. "07/23/2026 08:00 AM EDT"). The
    // date is the leading token of the authoritative Start/End Date column, so taking it is a
    // structured-field read, not a guess pulled from prose.
    private static DateOnly? ParseColumnDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var token = HtmlEntity
            .DeEntitize(raw)
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (token == null)
        {
            return null;
        }

        return DateOnly.TryParseExact(
            token,
            "MM/dd/yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date
        )
            ? date
            : null;
    }

    private static string ExtractSlug(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return string.Empty;
        }

        var path = href.Split('?', '#')[0].TrimEnd('/');
        if (path.Length == 0)
        {
            return string.Empty;
        }

        return path[(path.LastIndexOf('/') + 1)..];
    }

    private static string ToAbsoluteUrl(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return string.Empty;
        }

        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return href;
        }

        return CalendarHost + (href.StartsWith('/') ? href : "/" + href);
    }
}
