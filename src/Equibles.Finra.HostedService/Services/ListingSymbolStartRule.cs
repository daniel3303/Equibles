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

    /// <summary>
    /// The first date of the run that follows the last earlier run another issuer's filings
    /// account for, or null when no earlier run is another issuer's. A run is another issuer's
    /// only when that issuer stated the symbol within the run's dates and the listing's own issuer
    /// stated it no earlier than the run's end; the run the listing trades in now is never one.
    /// </summary>
    /// <param name="dates">The listing's stored dates under its current symbol, ascending.</param>
    public static DateOnly? StartsOn(
        IReadOnlyList<DateOnly> dates,
        Guid ownIssuerId,
        IReadOnlyCollection<SymbolClaim> claims
    )
    {
        var runs = Runs(dates);
        if (runs.Count < 2)
            return null;

        var ownFirst = claims
            .Where(claim => claim.EquityIssuerId == ownIssuerId)
            .Select(claim => (DateOnly?)claim.FirstFiled)
            .Min();
        var others = claims.Where(claim => claim.EquityIssuerId != ownIssuerId).ToList();
        // An earlier run is another issuer's when that issuer stated the symbol within the run's
        // dates and the listing's own issuer had not stated it by the run's end.
        var lastEarlierHolder = Enumerable
            .Range(0, runs.Count - 1)
            .Where(index =>
                !(ownFirst <= runs[index].End)
                && others.Any(claim =>
                    claim.FirstFiled.AddDays(-StatementSlackDays) <= runs[index].End
                    && claim.LastFiled.AddDays(StatementSlackDays) >= runs[index].Start
                )
            )
            .DefaultIfEmpty(-1)
            .Max();

        return lastEarlierHolder < 0 ? null : runs[lastEarlierHolder + 1].Start;
    }

    private static List<(DateOnly Start, DateOnly End)> Runs(IReadOnlyList<DateOnly> dates)
    {
        var runs = new List<(DateOnly Start, DateOnly End)>();
        if (dates.Count == 0)
            return runs;

        var start = dates[0];
        var previous = dates[0];
        foreach (var date in dates.Skip(1))
        {
            if (date.DayNumber - previous.DayNumber > GapDays)
            {
                runs.Add((start, previous));
                start = date;
            }
            previous = date;
        }
        runs.Add((start, previous));
        return runs;
    }
}
