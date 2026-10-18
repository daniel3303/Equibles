using System.Globalization;
using System.Text;
using Equibles.Congress.Data;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;

namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// Derives a filing member's BioGuide id from the legislator terms covering its filing dates.
/// A House member matches Representative terms for its stored seat, a senator matches Senator
/// terms, and either must pass the surname check; only a single legislator across every filing
/// date yields an id.
/// </summary>
public class CongressMemberLegislatorMatcher
{
    private static readonly HashSet<string> SuffixTokens = ["jr", "sr", "ii", "iii", "iv", "v"];

    private readonly ILookup<(string State, short District), LegislatorTermCandidate> _houseTerms;
    private readonly List<LegislatorTermCandidate> _senateTerms;

    public CongressMemberLegislatorMatcher(IEnumerable<LegislatorTermCandidate> terms)
    {
        var all = terms.ToList();
        _houseTerms = all.Where(term =>
                term.Chamber == CongressPosition.Representative && term.District != null
            )
            .ToLookup(term => (term.State, term.District.Value));
        _senateTerms = all.Where(term => term.Chamber == CongressPosition.Senator).ToList();
    }

    /// <summary>The single legislator the member's filings point at, or null.</summary>
    public string Match(CongressMemberMatchSubject member)
    {
        var filerTokens = NormalizeFilerName(member.Name);
        if (filerTokens.Count == 0 || member.FilingDates.Count == 0)
            return null;

        var candidates = new List<LegislatorTermCandidate>();
        if (CongressSeat.TryParse(member.StateDistrict, out var state, out var district))
            candidates.AddRange(_houseTerms[(state, district)]);
        if (member.Position == CongressPosition.Senator)
            candidates.AddRange(_senateTerms);

        var matched = candidates
            .Where(term =>
                member.FilingDates.Any(date => term.StartDate <= date && date < term.EndDate)
                && EndsWith(filerTokens, Normalize(term.LastName))
            )
            .Select(term => term.BioguideId)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToList();
        return matched.Count == 1 ? matched[0] : null;
    }

    /// <summary>
    /// True when the filed name ends with the legislator's surname, compared without accents,
    /// case, periods or apostrophes, with hyphens read as spaces and a trailing generational
    /// suffix dropped from the filed name.
    /// </summary>
    public static bool SurnameMatches(string filerName, string legislatorLastName) =>
        EndsWith(NormalizeFilerName(filerName), Normalize(legislatorLastName));

    private static bool EndsWith(IReadOnlyList<string> filerTokens, IReadOnlyList<string> surname)
    {
        if (surname.Count == 0 || filerTokens.Count < surname.Count)
            return false;

        var offset = filerTokens.Count - surname.Count;
        for (var i = 0; i < surname.Count; i++)
        {
            if (!string.Equals(filerTokens[offset + i], surname[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static List<string> NormalizeFilerName(string name)
    {
        var tokens = Normalize(name);
        while (tokens.Count > 2 && SuffixTokens.Contains(tokens[^1]))
            tokens.RemoveAt(tokens.Count - 1);
        return tokens;
    }

    private static List<string> Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return [];

        var builder = new StringBuilder(name.Length);
        foreach (var character in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            if (character is '.' or '\'' or '’')
                continue;
            builder.Append(character == '-' ? ' ' : char.ToLowerInvariant(character));
        }

        return builder
            .ToString()
            .Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }
}
