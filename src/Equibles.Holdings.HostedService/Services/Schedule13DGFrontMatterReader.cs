using System.Text.RegularExpressions;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Reads the statement-level answers of a pre-XML Schedule 13D or 13G from its flattened text:
/// the CUSIP, the event date, the class title and a 13G's rule checkboxes. Each answer is the
/// value filed next to its caption, before it ("67066G104 (CUSIP Number)") or after it
/// ("CUSIP No. 67066G104").
/// </summary>
public static partial class Schedule13DGFrontMatterReader
{
    // How far from its caption an answer may sit; only separators may lie between them.
    private const int AnswerReach = 160;

    /// <summary>
    /// The CUSIPs filed next to a CUSIP caption before <paramref name="end"/>, as filed (spaces and
    /// dashes removed, upper case), in order of first appearance.
    /// </summary>
    public static List<string> ReadCusips(string flat, int end)
    {
        var cusips = new List<string>();
        var text = flat[..Math.Min(end, flat.Length)];
        foreach (Match caption in CusipCaption().Matches(text))
        {
            string value = null;
            if (caption.Groups["before"].Success)
            {
                var from = Math.Max(0, caption.Index - AnswerReach);
                var match = CusipBefore().Match(text[from..caption.Index]);
                if (match.Success)
                    value = match.Groups["cusip"].Value;
            }
            else
            {
                var to = Math.Min(text.Length, caption.Index + caption.Length + AnswerReach);
                var match = CusipAfter().Match(text[(caption.Index + caption.Length)..to]);
                if (match.Success)
                    value = match.Groups["cusip"].Value;
            }

            if (value == null)
                continue;
            var normalized = Separator().Replace(value, string.Empty).ToUpperInvariant();
            if (!cusips.Contains(normalized))
                cusips.Add(normalized);
        }

        return cusips;
    }

    /// <summary>
    /// The event date filed next to its caption, or null with a reason when the caption is missing,
    /// carries no date, or carries two different ones.
    /// </summary>
    public static (DateOnly? Date, string Refusal) ReadEventDate(string flat, int end)
    {
        var text = flat[..Math.Min(end, flat.Length)];
        var caption = EventDateCaption().Match(text);
        if (!caption.Success)
            return (null, "no event date caption");

        var found = new HashSet<DateOnly>();
        var from = Math.Max(0, caption.Index - AnswerReach);
        var before = text[from..caption.Index];
        var beforeDates = Schedule13DGTextValues.FindDates(before);
        if (beforeDates.Count > 0)
        {
            var last = beforeDates[^1];
            if (OnlySeparators(before[(last.Index + last.Length)..]))
                found.Add(last.Date);
        }

        var afterStart = caption.Index + caption.Length;
        var after = text[afterStart..Math.Min(text.Length, afterStart + AnswerReach)];
        var afterDates = Schedule13DGTextValues.FindDates(after);
        if (afterDates.Count > 0 && OnlySeparators(after[..afterDates[0].Index]))
            found.Add(afterDates[0].Date);

        return found.Count switch
        {
            1 => (found.Single(), null),
            0 => (null, "no event date"),
            _ => (null, "two event dates"),
        };
    }

    /// <summary>The class title filed next to its caption, or null.</summary>
    public static string ReadClassTitle(string flat, int end)
    {
        var text = flat[..Math.Min(end, flat.Length)];
        var caption = ClassTitleCaption().Match(text);
        if (!caption.Success)
            return null;

        string title;
        if (caption.Groups["before"].Success)
        {
            var issuer = IssuerNameCaption().Match(text, 0, caption.Index);
            if (!issuer.Success)
                return null;
            title = text[(issuer.Index + issuer.Length)..caption.Index];
        }
        else
        {
            var start = caption.Index + caption.Length;
            var next = CusipCaption().Match(text, start);
            if (!next.Success || next.Index - start > AnswerReach)
                return null;
            title = text[start..next.Index];
        }

        title = Separator().Replace(title.Trim(), " ").Trim(' ', ':', '-', '_');
        return title.Length is > 0 and <= 200 ? title : null;
    }

    /// <summary>
    /// The 13d-1 rules a 13G's checkboxes select, e.g. "Rule 13d-1(b)". Empty when the boxes cannot
    /// be read: every rule must carry a known mark on the same side of its caption.
    /// </summary>
    public static List<string> ReadRules(string flat, int end)
    {
        var text = flat[..Math.Min(end, flat.Length)];
        var section = RuleSection().Match(text);
        if (!section.Success)
            return [];

        var start = section.Index + section.Length;
        var stop = Math.Min(text.Length, start + 600);
        // The three captions follow the instruction; a later "13d-1" belongs to other text.
        var mentions = RuleMention().Matches(text[start..stop]).Take(3).ToList();
        var letters = mentions
            .Select(mention => char.ToLowerInvariant(mention.Groups["rule"].Value[0]))
            .ToList();
        if (letters.Count != 3 || letters.Distinct().Count() != 3)
            return [];

        var leading = new List<bool?>();
        var trailing = new List<bool?>();
        for (var i = 0; i < mentions.Count; i++)
        {
            var mentionStart = start + mentions[i].Index;
            var mentionEnd = mentionStart + mentions[i].Length;
            var previousEnd =
                i == 0 ? start : start + mentions[i - 1].Index + mentions[i - 1].Length;
            var nextStart = i + 1 < mentions.Count ? start + mentions[i + 1].Index : stop;
            leading.Add(
                Schedule13DGTextValues.ReadCheckbox(
                    Schedule13DGTextValues.TrailingCheckbox(text[previousEnd..mentionStart])
                )
            );
            trailing.Add(
                Schedule13DGTextValues.ReadCheckbox(
                    Schedule13DGTextValues.LeadingCheckbox(text[mentionEnd..nextStart])
                )
            );
        }

        // Marks lead their captions or trail them; with marks on both sides the pairing is unknown.
        var marks =
            leading.All(mark => mark != null) && trailing[^1] == null ? leading
            : trailing.All(mark => mark != null) && leading[0] == null ? trailing
            : GroupedMarks(text, start, mentions);
        if (marks == null)
            return [];

        return letters
            .Where((_, index) => marks[index] == true)
            .Select(letter => $"Rule 13d-1({letter})")
            .ToList();
    }

    // A column of three boxes beside a column of the three captions ("☐ ☒ ☐ Rule 13d-1(b) Rule
    // 13d-1(c) Rule 13d-1(d)") pairs them in order.
    private static List<bool?> GroupedMarks(string text, int start, List<Match> mentions)
    {
        for (var i = 1; i < mentions.Count; i++)
        {
            var gap = text[
                (start + mentions[i - 1].Index + mentions[i - 1].Length)..(
                    start + mentions[i].Index
                )
            ];
            if (!string.IsNullOrWhiteSpace(gap))
                return null;
        }

        var boxes = ThreeMarks().Match(text[start..(start + mentions[0].Index)]);
        if (!boxes.Success)
            return null;
        var marks = boxes
            .Groups["mark"]
            .Captures.Select(capture => Schedule13DGTextValues.ReadCheckbox(capture.Value))
            .ToList();
        return marks.Count == 3 && marks.All(mark => mark != null) ? marks : null;
    }

    private static bool OnlySeparators(string text) => !text.Any(char.IsLetterOrDigit);

    [GeneratedRegex(@"[\s\-]+")]
    private static partial Regex Separator();

    [GeneratedRegex(@"^[\s:.]*(?:(?<mark>" + Schedule13DGTextValues.Mark + @")\s*){3}$")]
    private static partial Regex ThreeMarks();

    [GeneratedRegex(
        @"(?<before>\(\s*CUSIP\s+(?:NUMBER|NO\.?)\s*\))|\bCUSIP\s*(?:NUMBER|NO\.?|NOS\.?|#)?\s*(?:OF\s+CLASS\s+OF\s+SECURITIES)?\s*[:.]?",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex CusipCaption();

    // A CUSIP is six issuer characters, two issue characters and a check digit; filers space or
    // dash the groups. Only separators may stand between it and its caption.
    private const string CusipToken = @"[0-9A-Z]{6}[\s\-]?[0-9A-Z*@#]{2}[\s\-]?[0-9A-Z]";

    [GeneratedRegex(
        @"(?<![0-9A-Z])(?<cusip>" + CusipToken + @")[^0-9A-Za-z]*$",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex CusipBefore();

    [GeneratedRegex(
        @"^[^0-9A-Za-z]*(?<cusip>" + CusipToken + @")(?![0-9A-Z])",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex CusipAfter();

    [GeneratedRegex(
        @"DATE\s+OF\s+(?:THE\s+)?EVENT\s+WHICH\s+REQUIRES?\s+(?:THE\s+)?FILING(?:\s+OF\s+TH(?:IS|E)\s+(?:STATEMENT|SCHEDULE))?\s*\)?",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex EventDateCaption();

    [GeneratedRegex(
        @"(?<before>\(\s*TITLE\s+OF\s+CLASS\s+OF\s+SECURITIES\s*\))|\bTITLE\s+OF\s+CLASS\s+OF\s+SECURITIES\s*:",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex ClassTitleCaption();

    [GeneratedRegex(@"\(\s*NAME\s+OF\s+(?:THE\s+)?ISSUER\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex IssuerNameCaption();

    [GeneratedRegex(
        @"\bRULE\s+PURSUANT\s+TO\s+WHICH\s+TH(?:IS|E)\s+SCHEDULE\s+IS\s+FILED\s*[:.]?",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex RuleSection();

    [GeneratedRegex(
        @"(?:RULE\s*)?13\s*d\s*[-–—]\s*1\s*\(\s*(?<rule>[bcd])\s*\)",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex RuleMention();
}
