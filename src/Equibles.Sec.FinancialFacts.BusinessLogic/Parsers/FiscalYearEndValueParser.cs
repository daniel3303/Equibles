using System.Globalization;
using System.Text.RegularExpressions;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

/// <summary>Decodes source-declared gMonthDay values and registered recurring-date transforms.</summary>
internal static class FiscalYearEndValueParser
{
    private const string RegistryPrefix = "http://www.xbrl.org/inlineXBRL/transformation/";
    private const string MonthNames =
        "January|February|March|April|May|June|July|August|September|October|November|December|"
        + "Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Oct|Nov|Dec|"
        + "JANUARY|FEBRUARY|MARCH|APRIL|MAY|JUNE|JULY|AUGUST|SEPTEMBER|OCTOBER|NOVEMBER|DECEMBER|"
        + "JAN|FEB|MAR|APR|MAY|JUN|JUL|AUG|SEP|OCT|NOV|DEC";

    public static bool TryParse(
        string value,
        out DateOnly date,
        string format = null,
        string registry = null
    )
    {
        date = default;
        value = value?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 256)
            return false;
        if (string.IsNullOrWhiteSpace(format))
            return value.Length == 7
                && value.StartsWith("--", StringComparison.Ordinal)
                && DateOnly.TryParseExact(
                    "2000-" + value[2..],
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out date
                );

        var modern = registry is RegistryPrefix + "2020-02-12" or RegistryPrefix + "2022-02-16";
        var legacy = registry is RegistryPrefix + "2011-07-31" or RegistryPrefix + "2015-02-26";
        // Function names changed between registries; a QName is not authoritative without its namespace.
        var numericMonthFirst =
            modern && format == "date-month-day" || legacy && format == "datemonthday";
        var numericDayFirst =
            modern && format == "date-day-month" || legacy && format == "datedaymonth";
        var englishMonthFirst =
            modern && format == "date-monthname-day-en" || legacy && format == "datemonthdayen";
        var englishDayFirst =
            modern && format == "date-day-monthname-en" || legacy && format == "datedaymonthen";
        if (!(numericMonthFirst || numericDayFirst || englishMonthFirst || englishDayFirst))
            return false;

        var pattern =
            numericMonthFirst ? @"\A(?<month>[0-9]{1,2})[^0-9]+(?<day>[0-9]{1,2})\z"
            : numericDayFirst ? @"\A(?<day>[0-9]{1,2})[^0-9]+(?<month>[0-9]{1,2})\z"
            : englishMonthFirst
                ? @"\A(?<month>" + MonthNames + @")[^0-9]+(?<day>[0-9]{1,2})[a-zA-Z]{0,2}\z"
            : @"\A(?<day>[0-9]{1,2})[^0-9]+(?<month>" + MonthNames + @")\z";
        var match = Regex.Match(
            value,
            pattern,
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking
        );
        if (!match.Success)
            return false;
        var monthText = match.Groups["month"].Value;
        var month =
            numericMonthFirst || numericDayFirst
                ? int.Parse(monthText, CultureInfo.InvariantCulture)
                : Array.FindIndex(
                    CultureInfo.InvariantCulture.DateTimeFormat.MonthNames,
                    name => name.Equals(monthText, StringComparison.OrdinalIgnoreCase)
                ) + 1;
        if (month == 0)
            month =
                Array.FindIndex(
                    CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames,
                    name => name.Equals(monthText, StringComparison.OrdinalIgnoreCase)
                ) + 1;
        var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(2000, month))
            return false;
        date = new DateOnly(2000, month, day);
        return true;
    }
}
