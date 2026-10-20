using System.Globalization;
using System.Text.RegularExpressions;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Reads single cover-page answers of a pre-XML Schedule 13D or 13G: share counts, percents,
/// dates, CUSIPs and checkboxes. Every reader answers only from the value the form states where
/// the answer belongs and reports failure instead of choosing between candidates.
/// </summary>
public static partial class Schedule13DGTextValues
{
    // Words of the "number of shares beneficially owned by each reporting person with" side
    // heading, which a two-column text cover page interleaves with the power answers.
    private static readonly HashSet<string> SideHeadingWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "NUMBER",
        "OF",
        "SHARES",
        "BENEFICIALLY",
        "OWNED",
        "BY",
        "EACH",
        "REPORTING",
        "PERSON",
        "WITH",
    };

    private static readonly string[] ZeroWords = ["NONE", "NIL", "ZERO"];

    /// <summary>
    /// The share count a cover-page row states: the first answer after its label, ignoring
    /// footnote markers and the side heading. A dash-wrapped zero (<c>-0-</c>) and "None" read as
    /// zero; anything else that is not a number fails.
    /// </summary>
    public static bool TryReadShares(string segment, out long shares)
    {
        shares = 0;
        var answer = AnswerText(segment, dropSideHeading: true);
        if (answer.Length == 0)
            return false;

        if (DashZero().IsMatch(answer) || StartsWithWord(answer, ZeroWords))
            return true;

        var number = LeadingNumber().Match(answer);
        if (!number.Success)
            return false;
        // A percent in a share row is a misread row, never a count, and a space-grouped number
        // ("1 234 567") cannot be told from two answers.
        var rest = answer[number.Length..];
        if (rest.TrimStart().StartsWith('%') || SpaceGroup().IsMatch(rest))
            return false;

        if (
            !decimal.TryParse(
                number.Groups["value"].Value.Replace(",", string.Empty),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var value
            )
            || value > long.MaxValue
        )
            return false;

        shares = (long)value;
        return true;
    }

    /// <summary>
    /// A voting or dispositive power row: a share count, or zero when the row is left blank or
    /// answered "not applicable", which is how filers state that the person holds no such power.
    /// </summary>
    public static bool TryReadPower(string segment, out long shares)
    {
        var answer = AnswerText(segment, dropSideHeading: true);
        if (answer.Length == 0 || NotApplicable().IsMatch(answer))
        {
            shares = 0;
            return true;
        }

        return TryReadShares(segment, out shares);
    }

    /// <summary>
    /// The percent of class a cover page states. "Approximately" is kept as filed; a bound such as
    /// "less than 1%" states no value and reads as null; anything else that is not a number, or is
    /// above 100, fails.
    /// </summary>
    public static bool TryReadPercent(string segment, out decimal? percent)
    {
        percent = null;
        var answer = AnswerText(segment, dropSideHeading: false);
        answer = Approximately().Replace(answer, string.Empty).TrimStart();
        if (answer.Length == 0)
            return false;
        if (PercentBound().IsMatch(answer))
            return true;
        if (DashZero().IsMatch(answer) || StartsWithWord(answer, ZeroWords))
        {
            percent = 0;
            return true;
        }

        var number = LeadingPercent().Match(answer);
        if (
            !number.Success
            || !decimal.TryParse(
                number.Groups["value"].Value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var value
            )
            || value > 100m
        )
            return false;

        percent = value;
        return true;
    }

    /// <summary>
    /// The dates <paramref name="text"/> states, in order, read as month-name or US numeric dates.
    /// </summary>
    public static List<(int Index, int Length, DateOnly Date)> FindDates(string text)
    {
        var dates = new List<(int, int, DateOnly)>();
        foreach (Match match in DatePattern().Matches(text ?? string.Empty))
        {
            if (TryBuildDate(match, out var date))
                dates.Add((match.Index, match.Length, date));
        }

        return dates;
    }

    /// <summary>True when <paramref name="cusip"/> is nine characters with a valid check digit.</summary>
    public static bool IsValidCusip(string cusip)
    {
        if (cusip is not { Length: 9 })
            return false;

        var sum = 0;
        for (var i = 0; i < 8; i++)
        {
            var c = char.ToUpperInvariant(cusip[i]);
            int value;
            if (c is >= '0' and <= '9')
                value = c - '0';
            else if (c is >= 'A' and <= 'Z')
                value = c - 'A' + 10;
            else if (c == '*')
                value = 36;
            else if (c == '@')
                value = 37;
            else if (c == '#')
                value = 38;
            else
                return false;

            if (i % 2 == 1)
                value *= 2;
            sum += value / 10 + value % 10;
        }

        var check = cusip[8];
        return check is >= '0' and <= '9' && (10 - sum % 10) % 10 == check - '0';
    }

    /// <summary>
    /// A checkbox mark as filed: true when checked, false when empty, null when the text is not a
    /// mark this reader knows. Word processors file the same boxes as several glyphs, including
    /// symbol-font letters.
    /// </summary>
    public static bool? ReadCheckbox(string mark)
    {
        var compact = Whitespace().Replace(mark ?? string.Empty, string.Empty);
        return compact switch
        {
            "[X]" or "[x]" or "(X)" or "(x)" or "X" or "x" or "[✓]" or "[√]" => true,
            "☒" or "☑" or "✓" or "✔" or "√" or "þ" or "ý" or "■" or "⌧" or "n" => true,
            "[]" or "()" or "[_]" or "☐" or "□" or "◻" or "❑" or "❏" or "o" or "¨" or "q" => false,
            _ => null,
        };
    }

    /// <summary>The checkbox mark at the end of <paramref name="text"/>, or null.</summary>
    public static string TrailingCheckbox(string text)
    {
        var match = TrailingMark().Match(text ?? string.Empty);
        return match.Success ? match.Groups["mark"].Value : null;
    }

    /// <summary>The checkbox mark at the start of <paramref name="text"/>, or null.</summary>
    public static string LeadingCheckbox(string text)
    {
        var match = LeadingMark().Match(text ?? string.Empty);
        return match.Success ? match.Groups["mark"].Value : null;
    }

    // The answer with label leftovers removed: instructions, colons, footnote markers, asterisks.
    private static string AnswerText(string segment, bool dropSideHeading)
    {
        var text = SeeInstructions().Replace(segment ?? string.Empty, " ");
        text = SeparatorRun().Replace(text, " ");
        if (dropSideHeading)
            text = string.Join(
                ' ',
                Whitespace()
                    .Split(text)
                    .Where(word => !SideHeadingWords.Contains(word.TrimEnd(':')))
            );
        text = LeadingNoise().Replace(text.Trim(), string.Empty);
        return text.Trim();
    }

    private static bool StartsWithWord(string text, string[] words) =>
        words.Any(word =>
            text.StartsWith(word, StringComparison.OrdinalIgnoreCase)
            && (text.Length == word.Length || !char.IsLetterOrDigit(text[word.Length]))
        );

    private static bool TryBuildDate(Match match, out DateOnly date)
    {
        date = default;
        int year;
        int month;
        int day;
        if (match.Groups["mname"].Success)
        {
            month = MonthNumber(match.Groups["mname"].Value);
            day = int.Parse(match.Groups["mday"].Value, CultureInfo.InvariantCulture);
            year = int.Parse(match.Groups["myear"].Value, CultureInfo.InvariantCulture);
        }
        else if (match.Groups["dname"].Success)
        {
            month = MonthNumber(match.Groups["dname"].Value);
            day = int.Parse(match.Groups["dday"].Value, CultureInfo.InvariantCulture);
            year = int.Parse(match.Groups["dyear"].Value, CultureInfo.InvariantCulture);
        }
        else if (match.Groups["iso"].Success)
        {
            year = int.Parse(match.Groups["iy"].Value, CultureInfo.InvariantCulture);
            month = int.Parse(match.Groups["im"].Value, CultureInfo.InvariantCulture);
            day = int.Parse(match.Groups["id"].Value, CultureInfo.InvariantCulture);
        }
        else
        {
            month = int.Parse(match.Groups["nm"].Value, CultureInfo.InvariantCulture);
            day = int.Parse(match.Groups["nd"].Value, CultureInfo.InvariantCulture);
            year = int.Parse(match.Groups["ny"].Value, CultureInfo.InvariantCulture);
            if (year < 100)
                year += 2000;
        }

        if (month is < 1 or > 12 || year is < 1990 or > 2100)
            return false;
        if (day < 1 || day > DateTime.DaysInMonth(year, month))
            return false;

        date = new DateOnly(year, month, day);
        return true;
    }

    private static int MonthNumber(string name) =>
        name.Trim('.').ToUpperInvariant()[..3] switch
        {
            "JAN" => 1,
            "FEB" => 2,
            "MAR" => 3,
            "APR" => 4,
            "MAY" => 5,
            "JUN" => 6,
            "JUL" => 7,
            "AUG" => 8,
            "SEP" => 9,
            "OCT" => 10,
            "NOV" => 11,
            "DEC" => 12,
            _ => 0,
        };

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^(?:N/?A|NOT\s+APPLICABLE)\b\.?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex NotApplicable();

    // Rules drawn with characters between the rows of a text cover page.
    [GeneratedRegex(@"[-_=─━]{4,}|\.{4,}")]
    private static partial Regex SeparatorRun();

    [GeneratedRegex(@"\(\s*see\s+instructions?\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex SeeInstructions();

    // Colons, bullets and asterisks before the answer, and footnote markers such as "(1)" or "(a)".
    [GeneratedRegex(
        @"^(?:[:*•|_]+\s*|\(\s*[0-9a-z]{1,2}\s*\)\s*|\[\s*_*\s*\]\s*|\(\s*see\s+items?\s+\d+(?:\s*\([a-z0-9]{1,3}\))?(?:\s*(?:,|and|&)\s*\d+(?:\s*\([a-z0-9]{1,3}\))?)*\s*\)\s*)+",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex LeadingNoise();

    [GeneratedRegex(@"^[-–—]+\s*0\s*[-–—]+|^[-–—]{1,3}(?:\s|$)|^0(?![\d.,])")]
    private static partial Regex DashZero();

    [GeneratedRegex(@"^(?<value>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)(?![\d,])")]
    private static partial Regex LeadingNumber();

    [GeneratedRegex(@"^ \d{3}(?!\d)")]
    private static partial Regex SpaceGroup();

    [GeneratedRegex(@"^(?<value>\d{1,3}(?:\.\d+)?|\.\d+)(?![\d,])")]
    private static partial Regex LeadingPercent();

    [GeneratedRegex(@"^(?:approximately|approx\.?|about)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex Approximately();

    [GeneratedRegex(@"^(?:less\s+than|under|below|<)\s*\d", RegexOptions.IgnoreCase)]
    private static partial Regex PercentBound();

    [GeneratedRegex(
        @"(?<![A-Za-z0-9])(?:(?<mname>Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?\s*(?<mday>\d{1,2})(?:st|nd|rd|th)?\s*,?\s*(?<myear>\d{4})|(?<dday>\d{1,2})(?:st|nd|rd|th)?\s+(?<dname>Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?\s*,?\s*(?<dyear>\d{4})|(?<iso>(?<iy>\d{4})-(?<im>\d{1,2})-(?<id>\d{1,2}))|(?<nm>\d{1,2})\s*[/.-]\s*(?<nd>\d{1,2})\s*[/.-]\s*(?<ny>\d{4}|\d{2}))(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex DatePattern();

    /// <summary>The checkbox glyphs <see cref="ReadCheckbox"/> knows, as a pattern.</summary>
    internal const string Mark =
        @"\[\s*[xX✓√]?\s*\]|\(\s*[xX]?\s*\)|\[_\]|[☒☑☐□◻❑❏✓✔√þý■⌧¨]|(?<![\w])[xXoqn](?![\w])";

    [GeneratedRegex(@"(?<mark>" + Mark + @")\s*$")]
    private static partial Regex TrailingMark();

    [GeneratedRegex(@"^\s*(?<mark>" + Mark + @")")]
    private static partial Regex LeadingMark();
}
