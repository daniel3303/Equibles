using System.Globalization;
using System.Text.RegularExpressions;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.ReleaseStatements;

/// <summary>
/// Reads the figures an earnings release states for a new period, with no model: a release
/// table prints the new period beside a comparative period, and a row is placed on a concept
/// only when its comparative value equals that concept's fact on file and its label is the one
/// the filer's own statements give that concept. The value proves the table's scale and
/// period, the two together prove the line; everything they cannot prove stays out.
/// </summary>
public static partial class ReleaseStatementMatcher
{
    // A column is a statement's comparative column when this many of its rows, and this share
    // of them, equal facts of one period on file; fewer is coincidence.
    private const int MinAnchorRows = 3;
    private const decimal MinAnchorShare = 0.5m;

    // Figures are read only from a financial statement: a table whose comparative column
    // reproduces this many lines on file. Reconciliations and highlight tables match fewer, and
    // their adjusted rows equal the reported figure in one year and not the next.
    private const int MinStatementRows = 12;

    // The new column states the same rows as its comparative column.
    private const decimal MinRowOverlap = 0.8m;

    // A 52/53-week year moves a period end by up to a week against the calendar.
    private const int YearShiftToleranceDays = 10;

    // A column needs this many non-zero figures to be read as a value column.
    private const int MinColumnRows = 4;

    private static readonly decimal[] Scales = [1m, 1_000m, 1_000_000m];

    private static readonly Dictionary<string, int> Months = BuildMonths();

    private static readonly ILookup<(FactTaxonomy, string), string> AliasesByTag = BuildAliases();

    [GeneratedRegex(@"([A-Za-z]{3,9})\.?\s*(\d{1,2}),?\s*(\d{4})")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"(?<!\d)(20\d{2})(?!\d)")]
    private static partial Regex YearPattern();

    [GeneratedRegex(@"\d")]
    private static partial Regex DigitPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacePattern();

    public static IReadOnlyList<ReleaseFigure> Match(
        string releaseText,
        DateOnly releaseFiled,
        IReadOnlyCollection<OnFileFact> factsOnFile,
        IReadOnlyCollection<FiledStatementLine> filedLines
    )
    {
        if (string.IsNullOrWhiteSpace(releaseText) || factsOnFile.Count == 0)
            return [];
        if (filedLines.Count == 0)
            return [];

        var lines = filedLines
            .Select(l => (l.Taxonomy, l.Tag, NormalizeLabel(l.Label)))
            .ToHashSet();

        var onFile = new FactsOnFile(factsOnFile);
        if (onFile.LatestFlowEnd is not { } latestFlowEnd)
            return [];

        var claims = new List<Placement>();
        foreach (var table in ReleaseTable.ReadAll(releaseText))
        {
            if (table.Rows.Count >= MinColumnRows)
                claims.AddRange(ReadTable(table, onFile, lines, latestFlowEnd, releaseFiled));
        }

        // Every row whose comparative value equals a fact claims that fact's new period. Rows
        // that claim one figure at different values (a line that split in two this year, a
        // reported and an adjusted table) leave it unproven, whichever of them the label fits.
        var placed = claims
            .GroupBy(c => (c.Fact.ConceptId, c.Start, c.End))
            .Where(g => g.Select(c => c.Value).Distinct().Count() == 1)
            .Select(g => g.FirstOrDefault(c => c.Proven))
            .Where(c => c != null)
            .ToList();

        return Stamp(placed, onFile);
    }

    private static IEnumerable<Placement> ReadTable(
        ReleaseTable table,
        FactsOnFile onFile,
        HashSet<(FactTaxonomy, string, string)> lines,
        DateOnly latestFlowEnd,
        DateOnly releaseFiled
    )
    {
        var valueColumns = table
            .Rows.SelectMany(r => r.Values.Keys)
            .Distinct()
            .Where(c =>
                table.Rows.Count(r => r.Values.TryGetValue(c, out var v) && v != 0) >= MinColumnRows
            )
            .OrderBy(c => c)
            .ToList();

        var (scale, anchors) = Anchor(table, valueColumns, onFile);
        if (anchors.Count == 0)
            yield break;

        var headers = valueColumns.ToDictionary(
            c => c,
            c => ColumnHeader.Read(table.HeaderOver(c))
        );

        // Pair each unanchored column with the one new period its comparative columns agree on.
        var pairs = new Dictionary<int, (int Anchor, DateOnly Start, DateOnly End)>();
        foreach (var column in valueColumns.Where(c => !anchors.ContainsKey(c)))
        {
            var columnRows = RowsStating(table, column);
            var options = new List<(int Anchor, DateOnly Start, DateOnly End)>();
            foreach (var (anchorColumn, anchor) in anchors)
            {
                var overlap = (decimal)anchor.Rows.Count(columnRows.Contains) / anchor.Rows.Count;
                if (overlap < MinRowOverlap)
                    continue;
                if (
                    NewPeriod(
                        headers[column],
                        headers[anchorColumn],
                        anchor,
                        onFile,
                        latestFlowEnd,
                        releaseFiled
                    ) is
                    { } period
                )
                    options.Add((anchorColumn, period.Start, period.End));
            }

            if (options.Select(o => (o.Start, o.End)).Distinct().Count() == 1)
                pairs[column] = options[0];
        }

        // Two columns claiming one comparative column or one period are a reported/adjusted
        // split the dates cannot tell apart.
        var claims = pairs.Values.GroupBy(p => p.Anchor).ToDictionary(g => g.Key, g => g.Count());
        var targets = pairs
            .Values.GroupBy(p => (p.Start, p.End))
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var (column, pair) in pairs)
        {
            if (claims[pair.Anchor] > 1 || targets[(pair.Start, pair.End)] > 1)
                continue;

            var anchor = anchors[pair.Anchor];
            foreach (var (rowIndex, matches) in anchor.Matches)
            {
                var row = table.Rows[rowIndex];
                if (!row.Values.TryGetValue(column, out var cell))
                    continue;
                // A percentage or per-share column beside whole amounts is another measure.
                if (
                    ReleaseTable.HasDecimals(row.Cells[column])
                    != ReleaseTable.HasDecimals(row.Cells[pair.Anchor])
                )
                    continue;
                var label = NormalizeLabel(row.Label);
                var line =
                    anchor.Matches.Count >= MinStatementRows
                        ? SingleLine(rowIndex, label, anchors, lines)
                        : null;
                // A row the filer's label names as one line does not dispute the others its
                // value happens to equal (basic and diluted per-share figures that were equal
                // last year); a row with no such label disputes every fact it equals.
                var named = matches.Where(m => lines.Contains((m.Taxonomy, m.Tag, label))).ToList();
                foreach (var fact in named.Count > 0 ? named : matches)
                {
                    var flipped = fact.Value == -row.Values[pair.Anchor] * scale;
                    // A release prints an expense or a payment as a negative; that convention
                    // exists only for a line the taxonomy gives a debit or credit side. A
                    // per-share figure or a count printed with the other sign is a different
                    // number, not a convention.
                    if (flipped && fact.Balance == null)
                        continue;
                    yield return new Placement(
                        fact,
                        pair.Start,
                        pair.End,
                        flipped ? -cell * scale : cell * scale,
                        row.Line,
                        row.Label,
                        Proven: line == (fact.Taxonomy, fact.Tag)
                    );
                }
            }
        }
    }

    // The table's scale is the one whose comparative columns match the most rows.
    private static (decimal Scale, Dictionary<int, AnchoredColumn> Anchors) Anchor(
        ReleaseTable table,
        List<int> valueColumns,
        FactsOnFile onFile
    )
    {
        var best = (Scale: 1m, Anchors: new Dictionary<int, AnchoredColumn>(), Score: 0);
        foreach (var scale in Scales)
        {
            var anchors = new Dictionary<int, AnchoredColumn>();
            foreach (var column in valueColumns)
            {
                var rows = Enumerable
                    .Range(0, table.Rows.Count)
                    .Where(i => table.Rows[i].Values.TryGetValue(column, out var v) && v != 0)
                    .ToList();
                var byPeriod =
                    new Dictionary<
                        (DateOnly Start, DateOnly End),
                        Dictionary<int, List<OnFileFact>>
                    >();
                foreach (var rowIndex in rows)
                {
                    var value = table.Rows[rowIndex].Values[column] * scale;
                    foreach (var fact in onFile.Stating(value).Concat(onFile.Stating(-value)))
                    {
                        var period = (fact.PeriodStart, fact.PeriodEnd);
                        if (!byPeriod.TryGetValue(period, out var matched))
                            byPeriod[period] = matched = [];
                        if (!matched.TryGetValue(rowIndex, out var facts))
                            matched[rowIndex] = facts = [];
                        facts.Add(fact);
                    }
                }

                if (byPeriod.Count == 0)
                    continue;
                var winner = byPeriod
                    .OrderByDescending(p => p.Value.Count)
                    .ThenByDescending(p => p.Key.End)
                    .ThenByDescending(p => p.Key.Start)
                    .First();
                if (
                    winner.Value.Count >= MinAnchorRows
                    && (decimal)winner.Value.Count / rows.Count >= MinAnchorShare
                )
                    anchors[column] = new AnchoredColumn(
                        winner.Key.Start,
                        winner.Key.End,
                        winner.Value,
                        rows.ToHashSet()
                    );
            }

            var score = anchors.Values.Sum(a => a.Matches.Count(m => m.Value.Any(IsStandardLine)));
            if (score > best.Score)
                best = (scale, anchors, score);
        }

        return (best.Scale, best.Anchors);
    }

    // The row's line: the one tag whose fact on file equals the comparative value in every
    // comparative column and whose label in the filer's own statements is the row's label. A
    // comparative value alone also equals other lines' facts (a total and its only component,
    // an unrelated disclosure of the same amount), and a row can keep its amount's history
    // while changing what it states.
    private static (FactTaxonomy, string)? SingleLine(
        int rowIndex,
        string label,
        Dictionary<int, AnchoredColumn> anchors,
        HashSet<(FactTaxonomy, string, string)> lines
    )
    {
        HashSet<(FactTaxonomy, string)> tags = null;
        foreach (var anchor in anchors.Values)
        {
            if (!anchor.Matches.TryGetValue(rowIndex, out var matches))
                continue;
            var labelled = matches
                .Where(m => lines.Contains((m.Taxonomy, m.Tag, label)))
                .Select(m => (m.Taxonomy, m.Tag))
                .ToHashSet();
            if (tags == null)
                tags = labelled;
            else
                tags.IntersectWith(labelled);
        }

        if (tags is not { Count: 1 })
            return null;
        var tag = tags.Single();
        return AliasesByTag.Contains(tag) ? tag : null;
    }

    private static bool IsStandardLine(OnFileFact fact) =>
        AliasesByTag.Contains((fact.Taxonomy, fact.Tag));

    private static HashSet<int> RowsStating(ReleaseTable table, int column) =>
        Enumerable
            .Range(0, table.Rows.Count)
            .Where(i => table.Rows[i].Values.ContainsKey(column))
            .ToHashSet();

    private static (DateOnly Start, DateOnly End)? NewPeriod(
        ColumnHeader column,
        ColumnHeader anchorHeader,
        AnchoredColumn anchor,
        FactsOnFile onFile,
        DateOnly latestFlowEnd,
        DateOnly releaseFiled
    )
    {
        if (anchor.Start == anchor.End)
        {
            // A balance: the column states its own date, and the comparative column's date,
            // where stated, is the anchored one.
            if (column.Date is not { } asOf || asOf < latestFlowEnd || asOf > releaseFiled)
                return null;
            if (anchorHeader.Date is { } anchorDate && anchorDate != anchor.End)
                return null;
            return onFile.HasPeriod(asOf, asOf) ? null : (asOf, asOf);
        }

        // A flow: the same span one year on, so the same header but for the year.
        if (column.Year is not { } year || anchorHeader.Year is not { } anchorYear)
            return null;
        if (year != anchorYear + 1 || column.Signature != anchorHeader.Signature)
            return null;

        var targetEnd = anchor.End.AddYears(1);
        DateOnly end;
        if (column.Date is { } stated)
        {
            if (Math.Abs(stated.DayNumber - targetEnd.DayNumber) > YearShiftToleranceDays)
                return null;
            end = stated;
        }
        else if (IsMonthEnd(anchor.End))
            end = MonthEnd(targetEnd);
        else
            return null;
        if (end <= latestFlowEnd || end > releaseFiled)
            return null;

        // The span starts the day after a period end on file, else on the same first of month.
        var targetStart = anchor.Start.AddYears(1);
        DateOnly start;
        if (onFile.FlowEndNearestBefore(targetStart, YearShiftToleranceDays) is { } previousEnd)
            start = previousEnd.AddDays(1);
        else if (anchor.Start.Day == 1)
            start = targetStart;
        else
            return null;

        return onFile.HasPeriod(start, end) ? null : (start, end);
    }

    // Stamps follow the comparative period one fiscal year on; a balance takes the stamp of the
    // flow that ends on its date, else of last year's balance.
    private static IReadOnlyList<ReleaseFigure> Stamp(List<Placement> placed, FactsOnFile onFile)
    {
        // A year-end balance belongs to the year, not to its fourth quarter: the longest flow
        // ending on the date names it.
        var flowStamps = new Dictionary<DateOnly, (int Year, SecFiscalPeriod Period)>();
        var figures = new List<ReleaseFigure>();
        foreach (var placement in placed.Where(p => p.Start != p.End).OrderBy(p => p.Start))
        {
            var stamp = (placement.Fact.FiscalYear + 1, placement.Fact.FiscalPeriod);
            flowStamps.TryAdd(placement.End, stamp);
            figures.Add(placement.ToFigure(stamp.Item1, stamp.FiscalPeriod));
        }

        foreach (var placement in placed.Where(p => p.Start == p.End))
        {
            if (flowStamps.TryGetValue(placement.End, out var stamp))
                figures.Add(placement.ToFigure(stamp.Year, stamp.Period));
            else if (
                onFile.BalanceStampOneYearBefore(placement.End, YearShiftToleranceDays) is { } prior
            )
                figures.Add(placement.ToFigure(prior.Year + 1, prior.Period));
        }

        return figures;
    }

    private static string NormalizeLabel(string label) =>
        new(label.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool IsMonthEnd(DateOnly date) => date.AddDays(1).Day == 1;

    private static DateOnly MonthEnd(DateOnly date) =>
        new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));

    private static Dictionary<string, int> BuildMonths()
    {
        var months = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var names = CultureInfo.InvariantCulture.DateTimeFormat.MonthNames;
        for (var i = 0; i < 12; i++)
        {
            months[names[i]] = i + 1;
            months[names[i][..3]] = i + 1;
        }

        months["Sept"] = 9;
        return months;
    }

    private static ILookup<(FactTaxonomy, string), string> BuildAliases() =>
        FinancialConceptAliases
            .SupportedAliases.SelectMany(alias =>
                FinancialConceptAliases.TryResolve(alias, out var concepts)
                    ? concepts.Select(c => (Key: (c.Taxonomy, c.Tag), Alias: alias))
                    : []
            )
            .Distinct()
            .ToLookup(pair => pair.Key, pair => pair.Alias);

    private sealed record AnchoredColumn(
        DateOnly Start,
        DateOnly End,
        Dictionary<int, List<OnFileFact>> Matches,
        HashSet<int> Rows
    );

    private sealed record Placement(
        OnFileFact Fact,
        DateOnly Start,
        DateOnly End,
        decimal Value,
        int Line,
        string Label,
        bool Proven
    )
    {
        public ReleaseFigure ToFigure(int fiscalYear, SecFiscalPeriod fiscalPeriod) =>
            new(
                Fact.ConceptId,
                Fact.Unit,
                Start,
                End,
                Value,
                fiscalYear,
                fiscalPeriod,
                Line,
                Label
            );
    }

    // What a column's header states: its own date, its year, and its wording without digits,
    // which is equal for one span in two years ("Three Months Ended September 30,").
    private sealed record ColumnHeader(DateOnly? Date, int? Year, string Signature)
    {
        public static ColumnHeader Read(string text)
        {
            DateOnly? date = null;
            foreach (Match match in DatePattern().Matches(text))
            {
                if (!Months.TryGetValue(match.Groups[1].Value, out var month))
                    continue;
                var year = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                var day = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                if (day >= 1 && day <= DateTime.DaysInMonth(year, month))
                    date = new DateOnly(year, month, day);
            }

            var years = YearPattern().Matches(text);
            var signature = SpacePattern()
                .Replace(DigitPattern().Replace(text, string.Empty), " ")
                .Trim()
                .ToLowerInvariant();
            return new ColumnHeader(
                date,
                years.Count > 0 ? int.Parse(years[^1].Value, CultureInfo.InvariantCulture) : null,
                signature
            );
        }
    }

    private sealed class FactsOnFile
    {
        private readonly Dictionary<decimal, List<OnFileFact>> _byValue = [];
        private readonly HashSet<(DateOnly, DateOnly)> _periods = [];
        private readonly List<DateOnly> _flowEnds;
        private readonly List<OnFileFact> _balances;

        public FactsOnFile(IReadOnlyCollection<OnFileFact> facts)
        {
            foreach (var fact in facts)
                _periods.Add((fact.PeriodStart, fact.PeriodEnd));

            // A period the filer has stated at two values is no anchor: a retag or a recast
            // moved it, and the release may print either.
            foreach (
                var group in facts.GroupBy(f => (f.Taxonomy, f.Tag, f.PeriodStart, f.PeriodEnd))
            )
            {
                if (group.Select(f => f.Value).Distinct().Count() != 1)
                    continue;
                var fact = group.First();
                if (!_byValue.TryGetValue(fact.Value, out var stating))
                    _byValue[fact.Value] = stating = [];
                stating.Add(fact);
            }

            _flowEnds = facts
                .Where(f => f.PeriodEnd > f.PeriodStart)
                .Select(f => f.PeriodEnd)
                .Distinct()
                .OrderBy(d => d)
                .ToList();
            _balances = facts.Where(f => f.PeriodEnd == f.PeriodStart).ToList();
        }

        public DateOnly? LatestFlowEnd => _flowEnds.Count > 0 ? _flowEnds[^1] : null;

        public IEnumerable<OnFileFact> Stating(decimal value) =>
            _byValue.TryGetValue(value, out var facts) ? facts : [];

        public bool HasPeriod(DateOnly start, DateOnly end) => _periods.Contains((start, end));

        public DateOnly? FlowEndNearestBefore(DateOnly start, int toleranceDays)
        {
            DateOnly? nearest = null;
            foreach (var end in _flowEnds)
            {
                var gap = Math.Abs(end.AddDays(1).DayNumber - start.DayNumber);
                if (gap > toleranceDays)
                    continue;
                if (
                    nearest is not { } current
                    || gap < Math.Abs(current.AddDays(1).DayNumber - start.DayNumber)
                )
                    nearest = end;
            }

            return nearest;
        }

        public (int Year, SecFiscalPeriod Period)? BalanceStampOneYearBefore(
            DateOnly asOf,
            int toleranceDays
        )
        {
            var target = asOf.AddYears(-1);
            var prior = _balances
                .Where(f => Math.Abs(f.PeriodEnd.DayNumber - target.DayNumber) <= toleranceDays)
                .GroupBy(f => (f.FiscalYear, f.FiscalPeriod))
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            return prior?.Key;
        }
    }
}
