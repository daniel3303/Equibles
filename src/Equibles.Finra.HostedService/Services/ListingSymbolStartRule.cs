namespace Equibles.Finra.HostedService.Services;

/// <summary>
/// Reads the date a recycled symbol started naming its current listing from the listing's own
/// stored daily short-volume rows and the issuers that stated the symbol on SEC cover pages.
/// </summary>
public static class ListingSymbolStartRule
{
    // A quiet stretch longer than this splits a listing's rows into separate runs: a symbol that
    // passes from one security to the next leaves such a stretch between the two holders' rows.
    public const int GapDays = 30;

    // A holder keeps filing for a while after its last trade and may state its symbol shortly
    // before its first, so its filings vouch for the rows this many days either side of them.
    public const int StatementSlackDays = 30;

    public readonly record struct SymbolClaim(
        Guid EquityIssuerId,
        DateOnly FirstFiled,
        DateOnly LastFiled
    );

    /// <summary>The first and last stored dates of a run of a listing's daily rows.</summary>
    public readonly record struct RowRun(DateOnly Start, DateOnly End);

    /// <summary>A start and the earlier holder's run it rests on.</summary>
    public readonly record struct Derivation(DateOnly StartsOn, RowRun EarlierHolder);

    /// <summary>
    /// The first date of the run that follows the last earlier run another issuer's filings
    /// account for, or null when no earlier run is another issuer's (<see cref="Derive"/>).
    /// </summary>
    /// <param name="dates">The listing's stored dates under its current symbol, ascending.</param>
    public static DateOnly? StartsOn(
        IReadOnlyList<DateOnly> dates,
        Guid ownIssuerId,
        IReadOnlyCollection<SymbolClaim> claims
    ) => Derive(dates, ownIssuerId, claims)?.StartsOn;

    /// <summary>
    /// The start: the first date of the run that follows the last earlier run another issuer's
    /// filings account for, with that run, or null when no earlier run is another issuer's. A run is
    /// another issuer's only when that issuer stated the symbol within the run's dates, stopped
    /// stating it before the listing's current run began, and the listing's own issuer stated it no
    /// earlier than the run's end; the run the listing trades in now is never one.
    /// </summary>
    /// <param name="dates">The listing's stored dates under its current symbol, ascending.</param>
    public static Derivation? Derive(
        IReadOnlyList<DateOnly> dates,
        Guid ownIssuerId,
        IReadOnlyCollection<SymbolClaim> claims
    )
    {
        var runs = Runs(dates);
        if (runs.Count < 2)
            return null;

        var lastEarlierHolder = Enumerable
            .Range(0, runs.Count - 1)
            .Where(index => IsEarlierHolders(runs[index], runs[^1].Start, ownIssuerId, claims))
            .DefaultIfEmpty(-1)
            .Max();

        return lastEarlierHolder < 0
            ? null
            : new Derivation(runs[lastEarlierHolder + 1].Start, runs[lastEarlierHolder]);
    }

    /// <summary>
    /// Whether a start whose earlier rows were withdrawn still rests on its earlier holder's run.
    /// The run is passed as recorded, since its rows are gone, and the start never moves: a later
    /// first row would bar that row's return.
    /// </summary>
    /// <param name="startsOn">The recorded start.</param>
    /// <param name="earlierHolder">The recorded run the start rests on.</param>
    /// <param name="dates">The listing's remaining dates under its current symbol, ascending.</param>
    public static bool Holds(
        DateOnly startsOn,
        RowRun earlierHolder,
        IReadOnlyList<DateOnly> dates,
        Guid ownIssuerId,
        IReadOnlyCollection<SymbolClaim> claims
    )
    {
        var later = Runs(dates.Where(date => date > earlierHolder.End).ToList());
        var currentRun = later.Count > 0 ? later[^1].Start : startsOn;
        return IsEarlierHolders(earlierHolder, currentRun, ownIssuerId, claims);
    }

    // The run is another issuer's when that issuer stated the symbol within the run's dates and the
    // listing's own issuer had not stated it by the run's end. An issuer still stating the symbol
    // after the listing's current run began holds a security trading today: a co-registrant of the
    // same filing, or a line whose symbol drops to the same separator-free spelling (a preferred
    // INN-PF beside an OTC INNPF); it never held it before.
    private static bool IsEarlierHolders(
        RowRun run,
        DateOnly currentRun,
        Guid ownIssuerId,
        IReadOnlyCollection<SymbolClaim> claims
    )
    {
        var ownFirst = claims
            .Where(claim => claim.EquityIssuerId == ownIssuerId)
            .Select(claim => (DateOnly?)claim.FirstFiled)
            .Min();
        return !(ownFirst <= run.End)
            && claims.Any(claim =>
                claim.EquityIssuerId != ownIssuerId
                && claim.LastFiled.DayNumber - StatementSlackDays < currentRun.DayNumber
                && claim.FirstFiled.DayNumber - StatementSlackDays <= run.End.DayNumber
                && claim.LastFiled.DayNumber + StatementSlackDays >= run.Start.DayNumber
            );
    }

    private static List<RowRun> Runs(IReadOnlyList<DateOnly> dates)
    {
        var runs = new List<RowRun>();
        if (dates.Count == 0)
            return runs;

        var start = dates[0];
        var previous = dates[0];
        foreach (var date in dates.Skip(1))
        {
            if (date.DayNumber - previous.DayNumber > GapDays)
            {
                runs.Add(new RowRun(start, previous));
                start = date;
            }
            previous = date;
        }
        runs.Add(new RowRun(start, previous));
        return runs;
    }
}
