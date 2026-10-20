using System.Text.RegularExpressions;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Reads the reporting-person cover pages of a pre-XML Schedule 13D or 13G from its flattened
/// text. Each cover page is found by its row labels, never by row numbers (13D and 13G number the
/// same rows differently), and each answer is the text between its label and the next one.
/// </summary>
public static partial class Schedule13DGCoverPageReader
{
    // A cover page longer than this is prose that happens to repeat a label.
    private const int MaxCoverPageLength = 12_000;

    private enum Row
    {
        Name,
        Group,
        SecUseOnly,
        SourceOfFunds,
        LegalProceedings,
        Citizenship,
        SideHeading,
        SoleVoting,
        SharedVoting,
        SoleDispositive,
        SharedDispositive,
        Aggregate,
        Excludes,
        Percent,
        Type,
    }

    private sealed record Label(Row Row, int Start, int End);

    /// <summary>A cover page that could not be read, with the row that failed.</summary>
    public sealed record Failure(int CoverPage, string Reason);

    /// <summary>
    /// The reporting persons, in filed order, and the end of the last cover page; or a failure
    /// when a cover page is present but cannot be read in full.
    /// </summary>
    public static (List<Parsed13DGReportingPerson> Persons, int End, Failure Failure) Read(
        string flat
    )
    {
        var persons = new List<Parsed13DGReportingPerson>();
        var starts = NameLabel().Matches(flat).Select(match => match.Index).ToList();
        var end = 0;
        Failure firstFailure = null;
        for (var i = 0; i < starts.Count; i++)
        {
            var blockEnd = Math.Min(
                i + 1 < starts.Count ? starts[i + 1] : flat.Length,
                starts[i] + MaxCoverPageLength
            );
            var (person, coverEnd, reason) = ReadCoverPage(flat, starts[i], blockEnd);
            if (person != null)
            {
                // A cover page after an unreadable one means the unreadable one was a cover page.
                if (firstFailure != null)
                    return ([], 0, firstFailure);
                persons.Add(person);
                end = coverEnd;
                continue;
            }

            // Repeated labels after the cover pages are the body's prose; a numbered first row
            // marks a cover page, which must be readable.
            firstFailure ??= new Failure(persons.Count + 1, reason);
            if (HasRowMarker(flat, starts[i], "1"))
                return ([], 0, firstFailure);
        }

        return persons.Count == 0
            ? ([], 0, firstFailure ?? new Failure(0, "no cover page"))
            : (persons, end, null);
    }

    private static (Parsed13DGReportingPerson Person, int End, string Reason) ReadCoverPage(
        string flat,
        int start,
        int end
    )
    {
        var labels = FindLabels(flat, start, end, out var missing);
        if (labels == null)
            return (null, 0, $"no {missing} row");

        string Answer(Row row)
        {
            var index = labels.FindIndex(label => label.Row == row);
            if (index < 0)
                return null;
            var answerEnd = index + 1 < labels.Count ? labels[index + 1].Start : end;
            return flat[labels[index].End..answerEnd].Trim();
        }

        var name = CleanName(Answer(Row.Name));
        if (string.IsNullOrEmpty(name))
            return (null, 0, "no reporting person name");
        // A caption inside the name means the next row's caption was not recognized.
        if (CaptionInName().IsMatch(name))
            return (null, 0, "unreadable reporting person name");

        var person = new Parsed13DGReportingPerson
        {
            Name = name,
            CitizenshipOrOrganization = CleanCitizenship(Answer(Row.Citizenship)),
        };

        if (!Schedule13DGTextValues.TryReadPower(Answer(Row.SoleVoting), out var soleVoting))
            return (null, 0, "unreadable sole voting power");
        if (!Schedule13DGTextValues.TryReadPower(Answer(Row.SharedVoting), out var sharedVoting))
            return (null, 0, "unreadable shared voting power");
        if (!Schedule13DGTextValues.TryReadPower(Answer(Row.SoleDispositive), out var soleDisp))
            return (null, 0, "unreadable sole dispositive power");
        if (!Schedule13DGTextValues.TryReadPower(Answer(Row.SharedDispositive), out var shared))
            return (null, 0, "unreadable shared dispositive power");
        if (!Schedule13DGTextValues.TryReadShares(Answer(Row.Aggregate), out var aggregate))
            return (null, 0, "unreadable aggregate amount");
        if (!Schedule13DGTextValues.TryReadPercent(Answer(Row.Percent), out var percent))
            return (null, 0, "unreadable percent of class");

        // Beneficial ownership is voting or investment power, so a positive amount with every power
        // read as none means the answers were not where their captions are.
        if (aggregate > 0 && soleVoting + sharedVoting + soleDisp + shared == 0)
            return (null, 0, "no power accounts for the aggregate amount");

        person.SoleVotingPower = soleVoting;
        person.SharedVotingPower = sharedVoting;
        person.SoleDispositivePower = soleDisp;
        person.SharedDispositivePower = shared;
        person.AggregateAmountOwned = aggregate;
        person.PercentOfClass = percent;

        var typeLabel = labels[^1];
        var (codes, codesEnd) = ReadTypeCodes(flat, typeLabel.End, end);
        person.TypeCodes = codes;
        return (person, codesEnd, null);
    }

    // The rows in filed order, or null when a required row is missing. Optional rows are taken
    // only where they fall between the required rows around them.
    private static List<Label> FindLabels(string flat, int start, int end, out string missing)
    {
        missing = null;
        var labels = new List<Label>();
        var name = NameLabel().Match(flat, start);
        labels.Add(new Label(Row.Name, name.Index, name.Index + name.Length));
        var cursor = name.Index + name.Length;

        (Row Row, Regex Pattern, bool Required)[] rows =
        [
            (Row.Group, GroupLabel(), false),
            (Row.SecUseOnly, SecUseOnlyLabel(), false),
            (Row.SourceOfFunds, SourceOfFundsLabel(), false),
            (Row.LegalProceedings, LegalProceedingsLabel(), false),
            (Row.Citizenship, CitizenshipLabel(), false),
            (Row.SideHeading, SideHeadingLabel(), false),
            (Row.SoleVoting, SoleVotingLabel(), true),
            (Row.SharedVoting, SharedVotingLabel(), true),
            (Row.SoleDispositive, SoleDispositiveLabel(), true),
            (Row.SharedDispositive, SharedDispositiveLabel(), true),
            (Row.Aggregate, AggregateLabel(), true),
            (Row.Excludes, ExcludesLabel(), false),
            (Row.Percent, PercentLabel(), true),
            (Row.Type, TypeLabel(), true),
        ];

        for (var i = 0; i < rows.Length; i++)
        {
            var (row, pattern, required) = rows[i];
            var match = pattern.Match(flat, cursor, end - cursor);
            if (!match.Success)
            {
                if (!required)
                    continue;
                missing = RowName(row);
                return null;
            }

            if (!required)
            {
                // An optional row past the next required row belongs to a later page or the body.
                var nextRequired = rows.Skip(i + 1).First(next => next.Required);
                var bound = nextRequired.Pattern.Match(flat, cursor, end - cursor);
                if (bound.Success && match.Index > bound.Index)
                    continue;
            }

            labels.Add(
                new Label(row, WithRowMarker(flat, match.Index, cursor), match.Index + match.Length)
            );
            cursor = match.Index + match.Length;
        }

        return labels;
    }

    // Extends a label back over the row number filed before it ("6", "6.", "(6)"), so the number
    // is never read as the previous row's answer.
    private static int WithRowMarker(string flat, int labelStart, int floor)
    {
        var from = Math.Max(floor, labelStart - 12);
        var match = RowMarker().Match(flat[from..labelStart]);
        return match.Success ? from + match.Index : labelStart;
    }

    private static bool HasRowMarker(string flat, int labelStart, string number)
    {
        var from = Math.Max(0, labelStart - 12);
        var match = RowMarker().Match(flat[from..labelStart]);
        return match.Success && match.Groups["number"].Value == number;
    }

    private static (List<string> Codes, int End) ReadTypeCodes(string flat, int start, int end)
    {
        var codes = new List<string>();
        var cursor = start;
        foreach (Match token in CodeToken().Matches(flat[start..end]))
        {
            var text = token.Value.Trim().Trim(',', ';', '/', '&', ':', '.', '*');
            if (text.Length == 0 || text.Equals("AND", StringComparison.OrdinalIgnoreCase))
                continue;
            if (SeeInstructionsToken().IsMatch(text))
                continue;
            if (
                text.Length != 2
                || !text.All(char.IsUpper)
                || ReportingPersonTypeCodes.Parse([text], out var unrecognized)
                    == ReportingPersonTypes.None
                || unrecognized != null
            )
                break;
            if (!codes.Contains(text))
                codes.Add(text);
            cursor = start + token.Index + token.Length;
        }

        return (codes, cursor);
    }

    private static string CleanName(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return null;
        var name = IrsLabel().Replace(answer, " ");
        name = TaxId().Replace(name, " ");
        name = SeeInstructions().Replace(name, " ");
        name = Whitespace()
            .Replace(name, " ")
            .Trim()
            .Trim(':', '-', '–', '—', '_', '|', ',', ' ')
            .TrimStart('.', ' ');
        return name.Any(char.IsLetter) ? name : null;
    }

    // Two-column text cover pages interleave the side heading's first words with this answer.
    private static string CleanCitizenship(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return null;
        var text = SeparatorRun().Replace(answer, " ");
        text = TrailingSideHeading().Replace(text, string.Empty);
        text = Whitespace().Replace(text, " ").Trim().Trim(':', '-', '_', '|', '.', ' ');
        return text.Length == 0 ? null : text;
    }

    private static string RowName(Row row) =>
        row switch
        {
            Row.SoleVoting => "sole voting power",
            Row.SharedVoting => "shared voting power",
            Row.SoleDispositive => "sole dispositive power",
            Row.SharedDispositive => "shared dispositive power",
            Row.Aggregate => "aggregate amount",
            Row.Percent => "percent of class",
            Row.Type => "type of reporting person",
            _ => row.ToString(),
        };

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(
        @"\bMEMBER\s+OF\s+(?:A\s+)?GROUP\b|\bSEC\s+USE\b|\bCITIZENSHIP\b|\bSOURCE\s+OF\s+FUNDS\b|\bVOTING\s+POWER\b",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex CaptionInName();

    [GeneratedRegex(@"[-_=─━]{4,}")]
    private static partial Regex SeparatorRun();

    [GeneratedRegex(@"(?:\s+(?:NUMBER|OF|SHARES))+\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingSideHeading();

    [GeneratedRegex(@"(?<![\w,.])(?:\(\s*(?<number>\d{1,2})\s*\)|(?<number>\d{1,2})\s*[.):]?)\s*$")]
    private static partial Regex RowMarker();

    [GeneratedRegex(
        @"\bNAMES?\s+(?:AND\s+I\.?\s*R\.?\s*S\.?\s+IDENTIFICATION\s+NOS?\.?\s+)?OF\s+(?:THE\s+)?REPORTING\s+(?:PERSONS?|ENTIT(?:Y|IES))\b|\bITEM\s+1\s*:\s*REPORTING\s+PERSONS?\b",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex NameLabel();

    [GeneratedRegex(
        @"\bCHECK\s+(?:\S+\s+){0,3}?IF\s+(?:A\s+)?MEMBER\s+OF\s+(?:A\s+)?GROUP\b",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex GroupLabel();

    [GeneratedRegex(@"\bSEC\s+USE\s+ONLY\b", RegexOptions.IgnoreCase)]
    private static partial Regex SecUseOnlyLabel();

    [GeneratedRegex(@"\bSOURCE\s+OF\s+FUNDS\b", RegexOptions.IgnoreCase)]
    private static partial Regex SourceOfFundsLabel();

    [GeneratedRegex(
        @"\bCHECK\s+(?:BOX\s+)?IF\s+DISCLOSURE\s+OF\s+LEGAL\s+PROCEEDINGS\b",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex LegalProceedingsLabel();

    [GeneratedRegex(
        @"\bCITIZENSHIP(?:\s+(?:OR|OF|AND)\s+PLACE\s+(?:OF|OR)\s+ORGANI[SZ]ATION)?\b",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex CitizenshipLabel();

    [GeneratedRegex(
        @"\(\s*FOR\s+QUESTIONS[^)]{0,120}\)|\bNUMBER\s+OF\s*SHARES\s+BENEFICIALLY\s+OWNED\s+BY\s+(?:EACH\s+)?(?:THE\s+)?REPORTING\s+PERSONS?\s+WITH\b\s*:?",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex SideHeadingLabel();

    [GeneratedRegex(@"\bSOLE\s+VOTING\s+POWER\b", RegexOptions.IgnoreCase)]
    private static partial Regex SoleVotingLabel();

    [GeneratedRegex(@"\bSHARED\s+VOTING\s+POWER\b", RegexOptions.IgnoreCase)]
    private static partial Regex SharedVotingLabel();

    [GeneratedRegex(@"\bSOLE\s+DISPOSITIVE\s+POWER\b", RegexOptions.IgnoreCase)]
    private static partial Regex SoleDispositiveLabel();

    [GeneratedRegex(@"\bSHARED\s+DISPOSITIVE\s+POWER\b", RegexOptions.IgnoreCase)]
    private static partial Regex SharedDispositiveLabel();

    [GeneratedRegex(
        @"\bAGGREGATED?\s+AMOUNT\s+(?:BENEFICIALLY\s+)?OWNED(?:\s+BY\s+(?:EACH\s+)?(?:THE\s+)?REPORTING\s+(?:PERSONS?|ENTIT(?:Y|IES)))?\b",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex AggregateLabel();

    [GeneratedRegex(
        @"\b(?:CHECK\s+(?:BOX\s+)?IF\s+)?(?:THE\s+)?AGGREGATE\s+AMOUNT\s+(?:IN\s+ROW\s*\(?\s*\d{1,2}\s*\)?\s*)?EXCLUDES\s+CERTAIN\s+SHARES\b",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex ExcludesLabel();

    [GeneratedRegex(
        @"\bPERCENT(?:AGE)?\s+OF\s+(?:THE\s+)?CLASS\s+(?:REPRESENTED\s+(?:BY\s+)?(?:THE\s+)?(?:AMOUNT\s+)?(?:IN\s+)?ROW\s*\(?\s*\d{1,2}\s*\)?|OWNED\b)",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex PercentLabel();

    [GeneratedRegex(
        @"\bTYPE\s+OF\s+REPORTING\s+(?:PERSONS?|ENTIT(?:Y|IES))\b",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex TypeLabel();

    [GeneratedRegex(@"[^\s,;/&]+[,;/&]?")]
    private static partial Regex CodeToken();

    [GeneratedRegex(
        @"^\(?\s*SEE\b|^INSTRUCTIONS?\)?$|\(\s*SEE\s+INSTRUCTIONS?\s*\)",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex SeeInstructionsToken();

    [GeneratedRegex(@"\(\s*SEE\s+INSTRUCTIONS?\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex SeeInstructions();

    [GeneratedRegex(
        @"[,/]?\s*(?:\b(?:S\.?\s*S\.?\s+OR\s+)?I\.?\s*R\.?\s*S\.?\s*(?:IDENTIFICATION\s+NOS?\.?(?:\s+OF\s+(?:THE\s+)?ABOVE\s+PERSONS?)?(?:\s*\(\s*ENTITIES\s+ONLY\s*\))?|EMPLOYER\s+IDENTIFICATION\s+NO\.?|#|NOS?\.?|NUMBER)|\bEIN\b\s*(?:#|NO\.?|NUMBER)?)\s*:?",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex IrsLabel();

    [GeneratedRegex(@"(?<!\d)\d{2}-\d{7}(?!\d)")]
    private static partial Regex TaxId();
}
