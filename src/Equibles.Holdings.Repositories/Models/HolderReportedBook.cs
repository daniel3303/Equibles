using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.Repositories.Models;

/// <summary>
/// A filer's quarterly 13F book as it is reported today. A filer that stops filing because the top
/// of its stored reporting chain (<see cref="HolderRollup.ReportingTopCik"/>) reports its holdings
/// continues under that filer: the reporter's quarters from the one after its last, then its own.
/// Only a comparable book continues, so a parent that also reports other managers' books never
/// answers for one of them.
/// </summary>
public sealed record HolderReportedBook(
    InstitutionalHolder Holder,
    InstitutionalHolder Reporter,
    IReadOnlyList<HolderQuarterlySnapshot> Quarters
)
{
    /// <summary>Whether another filer reports the book now. Quarters are newest first either way.</summary>
    public bool Continued => Reporter.Id != Holder.Id;

    /// <summary>
    /// The handover has to be seamless to count: the reporter's 13F for the quarter right after
    /// the filer's last, with the two books within
    /// <see cref="HolderSuccession.MaximumComparableBookRatio"/> of each other.
    /// </summary>
    public static HolderReportedBook Continue(
        InstitutionalHolder holder,
        IEnumerable<HolderQuarterlySnapshot> own,
        InstitutionalHolder reporter,
        IEnumerable<HolderQuarterlySnapshot> reported
    )
    {
        var ownNewestFirst = own.OrderByDescending(quarter => quarter.ReportDate).ToList();
        var unchanged = new HolderReportedBook(holder, holder, ownNewestFirst);
        if (reporter == null || reporter.Id == holder.Id || ownNewestFirst.Count == 0)
            return unchanged;

        var last = ownNewestFirst[0];
        var next = QuarterEndAfter(last.ReportDate);
        var continued = reported
            .Where(quarter => quarter.ReportDate >= next)
            .OrderByDescending(quarter => quarter.ReportDate)
            .ToList();
        var takeover = continued.LastOrDefault();
        if (takeover == null || takeover.ReportDate != next || last.Aum <= 0 || takeover.Aum <= 0)
            return unchanged;

        var ratio = (double)Math.Max(last.Aum, takeover.Aum) / Math.Min(last.Aum, takeover.Aum);
        return ratio > HolderSuccession.MaximumComparableBookRatio
            ? unchanged
            : new HolderReportedBook(holder, reporter, [.. continued, .. ownNewestFirst]);
    }

    private static DateOnly QuarterEndAfter(DateOnly date)
    {
        var day = date.AddDays(1);
        var month = ((day.Month - 1) / 3 + 1) * 3;
        return new DateOnly(day.Year, month, DateTime.DaysInMonth(day.Year, month));
    }
}
