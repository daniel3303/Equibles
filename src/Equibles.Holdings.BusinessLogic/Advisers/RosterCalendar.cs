using Equibles.Sec.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Advisers;

/// <summary>
/// Which Form ADV roster was current at a date, from the roster ledger. A roster the history read
/// without advisers (an unreadable archive) counts like a month SEC published no data for.
/// </summary>
public sealed class RosterCalendar
{
    public sealed record Entry(
        FormAdvRegistration Registration,
        DateOnly ReportDate,
        bool Versioned,
        int AdviserCount
    );

    private readonly Dictionary<FormAdvRegistration, List<Entry>> _rosters;

    public RosterCalendar(IEnumerable<Entry> entries)
    {
        _rosters = entries
            .Where(entry => !(entry.Versioned && entry.AdviserCount == 0))
            .GroupBy(entry => entry.Registration)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(entry => entry.ReportDate).ToList()
            );
    }

    /// <summary>
    /// The roster of a kind current at <paramref name="date"/>, when the history has read it;
    /// null before the first roster or while that roster waits for the history.
    /// </summary>
    public DateOnly? CurrentAt(FormAdvRegistration registration, DateOnly date)
    {
        if (!_rosters.TryGetValue(registration, out var rosters))
            return null;
        var current = rosters.LastOrDefault(entry => entry.ReportDate <= date);
        return current is { Versioned: true } ? current.ReportDate : null;
    }
}
