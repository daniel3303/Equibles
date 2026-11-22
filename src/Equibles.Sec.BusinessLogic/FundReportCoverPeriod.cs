using System.Text.RegularExpressions;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.BusinessLogic;

// Form N-CSR's cover states the period the shareholder report covers ("Date of reporting period"). EDGAR's index
// leaves that period blank for some filings, so the registrant's own statement in the stored text dates them instead.
public static class FundReportCoverPeriod
{
    public static IReadOnlyList<DocumentType> Forms { get; } =
        Array.AsReadOnly<DocumentType>([
            DocumentType.NCsr,
            DocumentType.NCsrA,
            DocumentType.NCsrs,
            DocumentType.NCsrsA,
        ]);

    // The cover opens the filing; a later mention, such as a cover quoted inside an exhibit, never counts.
    private const int CoverLength = 20_000;

    private static readonly Regex Label = new(
        @"date\s+of\s+reporting\s+period",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
    );

    // A two-digit year is this century's: Form N-CSR dates from 2003. A written date's day and year share a line, so a
    // year opening the next paragraph never completes it; converted covers may break between the month and the day.
    private const string Day =
        @"(?:\d{1,2}/\d{1,2}/(?:\d{4}|\d{2})(?!\d)|(?:January|February|March|April|May|June|July|August|September"
        + @"|October|November|December|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sept|Sep|Oct|Nov|Dec)\.?\s+\d{1,2},?[^\S\r\n]+\d{4})";

    // The statement directly after the label: one day, a range ending in the period, or the periods of several series
    // filed together, whose latest is the filing's period as EDGAR states it. Markdown punctuation (a table cell, bold,
    // a colon or a line break) may separate it from the label.
    private static readonly Regex Statement = new(
        @"\G[\s:|*_]*(?<day>"
            + Day
            + @")(?:[\s*_]*(?:through|thru|to|and|&|,|[\-‐-—])[\s*_]*(?<day>"
            + Day
            + @"))*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
    );

    private static readonly Regex Numeric = new(
        @"^(?<month>\d{1,2})/(?<day>\d{1,2})/(?<year>\d{4}|\d{2})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled
    );

    private static readonly Regex Written = new(
        @"^(?<month>[a-z]{3})[a-z]*\.?\s+(?<day>\d{1,2}),?\s+(?<year>\d{4})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
    );

    private static readonly string[] MonthPrefixes =
    [
        "jan",
        "feb",
        "mar",
        "apr",
        "may",
        "jun",
        "jul",
        "aug",
        "sep",
        "oct",
        "nov",
        "dec",
    ];

    public static bool AppliesTo(DocumentType documentType) => Forms.Contains(documentType);

    /// <summary>The period the cover states, or null when the first label states no full date or a date after the
    /// filing.</summary>
    public static DateOnly? Read(string text, DateOnly filedOn)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        var cover = text.Length > CoverLength ? text[..CoverLength] : text;
        var label = Label.Match(cover);
        if (!label.Success)
            return null;

        var statement = Statement.Match(cover, label.Index + label.Length);
        if (!statement.Success)
            return null;

        var days = statement
            .Groups["day"]
            .Captures.Select(capture => Parse(capture.Value))
            .ToList();
        if (days.Any(day => day == null))
            return null;
        var period = days.Max()!.Value;
        return period <= filedOn ? period : null;
    }

    private static DateOnly? Parse(string value)
    {
        value = Regex.Replace(value, @"\s+", " ");
        int month;
        var match = Numeric.Match(value);
        if (match.Success)
        {
            month = int.Parse(match.Groups["month"].Value);
        }
        else
        {
            match = Written.Match(value);
            if (!match.Success)
                return null;
            month =
                Array.IndexOf(MonthPrefixes, match.Groups["month"].Value.ToLowerInvariant()) + 1;
        }

        var year = int.Parse(match.Groups["year"].Value);
        if (match.Groups["year"].Value.Length == 2)
            year += 2000;
        var day = int.Parse(match.Groups["day"].Value);
        if (
            month is < 1 or > 12
            || year < 1900
            || day < 1
            || day > DateTime.DaysInMonth(year, month)
        )
            return null;
        return new DateOnly(year, month, day);
    }
}
